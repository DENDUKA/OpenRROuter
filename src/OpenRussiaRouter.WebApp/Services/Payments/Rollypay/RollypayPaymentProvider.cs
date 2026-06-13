using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Services.Payments.Rollypay;

/// <summary>
/// <see cref="IPaymentProvider"/> adapter for the Rollypay СБП aggregator. Translates between the
/// provider-agnostic payment seam and Rollypay's REST API + webhook contract. All real money for
/// self-serve top-ups flows through here.
/// </summary>
/// <remarks>
/// <para>The provider is a singleton (it is captured by the singleton
/// <see cref="PaymentProviderRegistry"/>). It therefore does not hold an <see cref="HttpClient"/>;
/// it builds a short-lived <see cref="RollypayClient"/> per call from a factory-managed named
/// client so handler rotation keeps working.</para>
/// <para>Activated only when <c>Payments:ActiveProvider="rollypay"</c>. With no
/// <c>SigningSecret</c> configured, <see cref="Verify"/> fails every webhook (401); with no
/// <c>ApiKey</c>, outbound calls throw. Both fail safe, so registering it unconditionally is fine.</para>
/// </remarks>
public sealed class RollypayPaymentProvider : IPaymentProvider
{
    /// <summary>Named <see cref="HttpClient"/> registered in <c>Program.cs</c> (base address + timeout).</summary>
    public const string HttpClientName = "Rollypay";

    private static readonly JsonSerializerOptions WebhookJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<PaymentSettings> _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RollypayPaymentProvider> _logger;

    public RollypayPaymentProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<PaymentSettings> settings,
        ILogger<RollypayPaymentProvider> logger,
        TimeProvider? timeProvider = null)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => "rollypay";

    public async Task<PaymentCreated> CreateAsync(PaymentRequest request, CancellationToken ct)
    {
        var settings = _settings.CurrentValue.Rollypay;
        var client = NewClient(settings);

        // Send EXACTLY the amount/currency Billing recorded for this payment — never override the
        // currency here. Billing computed credited_usd from this same (amount, currency), so any
        // divergence would credit one amount while charging another. СБП settles in rubles, so the
        // checkout is responsible for forcing RUB; if a non-RUB currency reaches here, Rollypay
        // rejects it (400) and no money moves — fail-safe, not a silent mismatch.
        var apiRequest = new CreateRollypayPaymentRequest
        {
            Amount = request.Amount.ToString("F2", CultureInfo.InvariantCulture),
            PaymentCurrency = request.Currency,
            PaymentMethod = "sbp",
            OrderId = request.OrderId,
            Description = request.Description,
            SuccessRedirectUrl = NullIfEmpty(settings.SuccessRedirectUrl),
            FailRedirectUrl = NullIfEmpty(settings.FailRedirectUrl),
        };

        var payment = await client.CreatePaymentAsync(apiRequest, ct);

        if (string.IsNullOrWhiteSpace(payment.PaymentId))
        {
            throw new InvalidOperationException("Rollypay create payment returned no payment_id.");
        }
        if (string.IsNullOrWhiteSpace(payment.PayUrl))
        {
            throw new InvalidOperationException("Rollypay create payment returned no pay_url.");
        }

        _logger.LogInformation(
            "Rollypay payment created: order={OrderId} providerRef={ProviderPaymentId} amount={Amount} {Currency}",
            request.OrderId, payment.PaymentId, apiRequest.Amount, apiRequest.PaymentCurrency);

        return new PaymentCreated(payment.PaymentId, payment.PayUrl!, payment.ExpiresAt);
    }

    public WebhookVerificationResult Verify(string rawBody, IHeaderDictionary headers)
    {
        var settings = _settings.CurrentValue.Rollypay;
        var timestamp = headers["X-Timestamp"].FirstOrDefault();
        var signature = headers["X-Signature"].FirstOrDefault();

        var result = RollypayWebhookVerifier.Verify(
            settings.SigningSecret, rawBody, timestamp, signature, _timeProvider.GetUtcNow());

        if (result != RollypayVerificationResult.Valid)
        {
            _logger.LogWarning("Rollypay webhook verification failed: {Reason}", result);
            return WebhookVerificationResult.Invalid;
        }

        return WebhookVerificationResult.Valid;
    }

    public PaymentWebhookEvent Parse(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            throw new InvalidOperationException("Rollypay webhook payload is empty.");
        }

        RollypayWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RollypayWebhookPayload>(rawBody, WebhookJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Rollypay webhook payload is not valid JSON.", ex);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.EventType))
        {
            throw new InvalidOperationException("Rollypay webhook payload is missing event_type.");
        }

        var eventType = MapEventType(payload.EventType);
        var amount = ParseDecimal(payload.Amount);

        // Rollypay does not supply an idempotency / event id, so we leave ProviderEventId null and
        // let Billing fall back to (provider, provider_payment_id, event_type) deduplication.
        return new PaymentWebhookEvent(
            ProviderPaymentId: NullIfEmpty(payload.PaymentId),
            OrderId: NullIfEmpty(payload.OrderId),
            EventType: eventType,
            ConfirmedAmount: amount,
            ConfirmedCurrency: NullIfEmpty(payload.Currency),
            ProviderEventId: null,
            Reason: eventType == PaymentWebhookEventTypes.Paid ? null : NullIfEmpty(payload.Status));
    }

    public async Task<ProviderPaymentStatus> GetPaymentAsync(string providerPaymentId, CancellationToken ct)
    {
        var settings = _settings.CurrentValue.Rollypay;
        var client = NewClient(settings);

        var payment = await client.GetPaymentAsync(providerPaymentId, ct);

        return new ProviderPaymentStatus(
            Status: MapStatus(payment.Status),
            ConfirmedAmount: ParseDecimal(payment.Amount),
            ConfirmedCurrency: NullIfEmpty(payment.PaymentCurrency),
            ProviderEventId: null,
            Reason: null);
    }

    private RollypayClient NewClient(RollypaySettings settings) =>
        new(_httpClientFactory.CreateClient(HttpClientName), settings);

    /// <summary>Map a Rollypay <c>event_type</c> to the canonical lowercase event the pipeline consumes.</summary>
    /// <remarks>
    /// Account-level / informational events (<c>payout.completed</c>, <c>refund_request.completed</c>) and
    /// anything unrecognised pass through lowercased — the <see cref="PaymentWebhookProcessor"/> treats
    /// non-canonical event types as no-ops.
    /// </remarks>
    private static string MapEventType(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "payment.paid" => PaymentWebhookEventTypes.Paid,
        "payment.canceled" or "payment.cancelled" => PaymentWebhookEventTypes.Canceled,
        "payment.expired" => PaymentWebhookEventTypes.Expired,
        "payment.refunded" => PaymentWebhookEventTypes.Refunded,
        "payment.chargeback" => PaymentWebhookEventTypes.Chargeback,
        "payment.created" => PaymentWebhookEventTypes.Created,
        var other => other,
    };

    private static ProviderPaymentStatusType MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "created" => ProviderPaymentStatusType.Created,
        "processing" => ProviderPaymentStatusType.Pending,
        "paid" => ProviderPaymentStatusType.Paid,
        "canceled" or "cancelled" => ProviderPaymentStatusType.Canceled,
        "expired" => ProviderPaymentStatusType.Expired,
        "refunded" => ProviderPaymentStatusType.Refunded,
        "chargeback" => ProviderPaymentStatusType.ChargedBack,
        _ => ProviderPaymentStatusType.Unknown,
    };

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
