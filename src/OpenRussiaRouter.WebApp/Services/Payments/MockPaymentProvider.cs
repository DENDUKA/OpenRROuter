using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Services.Payments;

/// <summary>
/// In-process payment provider for Development and e2e tests. No real money moves — the
/// <c>PayUrl</c> points at a fake-checkout page inside WebApp itself, which then fires a
/// self-webhook to exercise the full pipeline.
/// </summary>
/// <remarks>
/// <para><b>Registration:</b> <see cref="MockPaymentProvider"/> MUST be registered only in
/// Development. <see cref="PaymentStartupGuard"/> fails fast if it ends up as the active
/// provider in Production.</para>
/// <para><b>Verification:</b> the mock expects a non-empty <c>X-Mock-Signature</c> header
/// matching <c>Mock.SharedSecret</c>. This is not cryptographic — it exists purely so the
/// webhook pipeline exercises the verify-then-parse path.</para>
/// </remarks>
public sealed class MockPaymentProvider : IPaymentProvider
{
    private readonly ILogger<MockPaymentProvider> _logger;
    private readonly MockPaymentSettings _settings;

    public MockPaymentProvider(
        IOptions<PaymentSettings> options,
        ILogger<MockPaymentProvider> logger)
    {
        _settings = options.Value.Mock;
        _logger = logger;
    }

    public string Name => "mock";

    /// <summary>The mock charges nothing, so it happily "settles" in whatever the user picked.</summary>
    public string? SettlementCurrency => null;

    public Task<PaymentCreated> CreateAsync(PaymentRequest request, CancellationToken ct)
    {
        // Provider-side id is a fresh GUID prefixed with "mock_" so it cannot collide with
        // any real provider's id space.
        var providerPaymentId = "mock_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        // Fake-checkout page is served by WebApp itself (TS-007 will add the Razor page).
        // The URL is stable and provider-agnostic — the mock owns the /pay/mock/* route.
        var payUrl = $"/pay/mock/{Uri.EscapeDataString(request.OrderId)}?ref={Uri.EscapeDataString(providerPaymentId)}";

        _logger.LogInformation(
            "Mock payment created: order={OrderId} providerRef={ProviderPaymentId} amount={Amount} {Currency}",
            request.OrderId, providerPaymentId, request.Amount, request.Currency);

        return Task.FromResult(new PaymentCreated(providerPaymentId, payUrl, ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1)));
    }

    public WebhookVerificationResult Verify(string rawBody, IHeaderDictionary headers)
    {
        var presented = headers["X-Mock-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(presented))
        {
            return WebhookVerificationResult.Invalid;
        }

        // Constant-time compare to avoid trivial timing oracles, even though the secret is weak.
        // The mock is dev-only — this is hygiene, not real defence.
        return FixedTimeEquals(presented, _settings.SharedSecret)
            ? WebhookVerificationResult.Valid
            : WebhookVerificationResult.Invalid;
    }

    public PaymentWebhookEvent Parse(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            throw new InvalidOperationException("Mock webhook payload is empty.");
        }

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        string GetString(string name) =>
            root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() ?? string.Empty
                : string.Empty;

        decimal? GetDecimal(string name) =>
            root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetDecimal()
                : (decimal?)null;

        var providerPaymentId = GetString("provider_payment_id");
        var orderId = GetString("order_id");
        var eventType = GetString("event_type");
        var currency = GetString("currency");
        var providerEventId = GetString("provider_event_id");
        var reason = GetString("reason");
        var amount = GetDecimal("amount");

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new InvalidOperationException("Mock webhook payload is missing event_type.");
        }

        return new PaymentWebhookEvent(
            ProviderPaymentId: string.IsNullOrWhiteSpace(providerPaymentId) ? null : providerPaymentId,
            OrderId: string.IsNullOrWhiteSpace(orderId) ? null : orderId,
            EventType: eventType.ToLowerInvariant(),
            ConfirmedAmount: amount,
            ConfirmedCurrency: string.IsNullOrWhiteSpace(currency) ? null : currency,
            ProviderEventId: string.IsNullOrWhiteSpace(providerEventId) ? null : providerEventId,
            Reason: string.IsNullOrWhiteSpace(reason) ? null : reason);
    }

    public Task<ProviderPaymentStatus> GetPaymentAsync(string providerPaymentId, CancellationToken ct)
    {
        // The mock has no state of its own — once a payment is created, the only way it transitions
        // is via the self-webhook from the fake-checkout page. Reconciliation therefore observes
        // the payment as still "Created" — which is correct: if the user never completed checkout,
        // the payment should remain in Created until it expires or is canceled.
        _logger.LogDebug(
            "Mock GetPaymentAsync: returning Created for {ProviderPaymentId} (mock is stateless).",
            providerPaymentId);

        return Task.FromResult(new ProviderPaymentStatus(
            ProviderPaymentStatusType.Created,
            ConfirmedAmount: null,
            ConfirmedCurrency: null,
            ProviderEventId: null,
            Reason: null));
    }

    /// <summary>
    /// Helper for the fake-checkout page (TS-007): build the JSON payload that the page will POST
    /// back to <c>/webhook/mock</c>. Exposed as a static so the page and tests share one source of truth.
    /// </summary>
    public static string BuildWebhookPayload(
        string providerPaymentId,
        string orderId,
        string eventType,
        decimal amount,
        string currency,
        string? providerEventId = null,
        string? reason = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("provider_payment_id", providerPaymentId);
            writer.WriteString("order_id", orderId);
            writer.WriteString("event_type", eventType);
            writer.WriteNumber("amount", amount);
            writer.WriteString("currency", currency);
            if (!string.IsNullOrEmpty(providerEventId))
            {
                writer.WriteString("provider_event_id", providerEventId);
            }
            if (!string.IsNullOrEmpty(reason))
            {
                writer.WriteString("reason", reason);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }
}
