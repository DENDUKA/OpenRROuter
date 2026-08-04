using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Services.Payments.YandexPay;

/// <summary>
/// <see cref="IPaymentProvider"/> adapter for Yandex Pay sandbox.
/// Card-only, RUB settlement. Creates orders via sandbox Merchant API and maps
/// webhook/order status to canonical payment events.
/// </summary>
/// <remarks>
/// <para>Sandbox only: uses <c>Authorization: Api-Key {merchantId}</c> per Yandex Pay docs.
/// No separate API key or signing secret needed for sandbox.</para>
/// <para>The webhook verifier handles JWT ES256 validation externally — <see cref="Verify"/>
/// is a pass-through since the endpoint calls the verifier directly.</para>
/// </remarks>
public sealed class YandexPayPaymentProvider : IPaymentProvider
{
    public const string HttpClientName = "YandexPay";

    private readonly YandexPayClient _client;
    private readonly YandexPaySettings _settings;
    private readonly ILogger<YandexPayPaymentProvider> _logger;

    public YandexPayPaymentProvider(
        YandexPayClient client,
        IOptions<PaymentSettings> options,
        ILogger<YandexPayPaymentProvider> logger)
    {
        _client = client;
        _settings = options.Value.YandexPay;
        _logger = logger;
    }

    public string Name => "yandexpay";

    /// <summary>Yandex Pay settles in rubles only — the checkout converts any display currency to RUB.</summary>
    public string? SettlementCurrency => "RUB";

    public async Task<PaymentCreated> CreateAsync(PaymentRequest request, CancellationToken ct)
    {
        var order = await _client.CreateOrderAsync(
            orderId: request.OrderId,
            amount: request.Amount,
            successRedirectUrl: _settings.SuccessRedirectUrl,
            failRedirectUrl: _settings.FailRedirectUrl,
            ttlSeconds: _settings.OrderTtlSeconds,
            ct);

        _logger.LogInformation(
            "Yandex Pay payment created: order={OrderId} paymentUrl received, amount={Amount} RUB.",
            request.OrderId, request.Amount);

        return new PaymentCreated(
            ProviderPaymentId: request.OrderId,
            PayUrl: order.PaymentUrl!);
    }

    /// <summary>
    /// Verify webhook signature. The endpoint calls <see cref="YandexPayWebhookVerifier"/>
    /// directly, so this is a pass-through.
    /// </summary>
    public WebhookVerificationResult Verify(string rawBody, IHeaderDictionary headers)
        => WebhookVerificationResult.Valid;

    /// <summary>
    /// Parse is a no-op — the endpoint handles verification and parsing via
    /// <see cref="YandexPayWebhookVerifier"/> before calling this provider.
    /// </summary>
    public PaymentWebhookEvent Parse(string rawBody)
        => throw new NotSupportedException("Yandex Pay webhook parsing is handled by the endpoint.");

    public async Task<ProviderPaymentStatus> GetPaymentAsync(string providerPaymentId, CancellationToken ct)
    {
        try
        {
            var order = await _client.GetOrderAsync(providerPaymentId, ct);
            return MapOrderStatus(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Yandex Pay: GetPaymentAsync failed for order {OrderId}.", providerPaymentId);
            return new ProviderPaymentStatus(
                ProviderPaymentStatusType.Unknown, null, null, null, null);
        }
    }

    /// <summary>
    /// Map Yandex Pay order status to canonical payment status.
    /// </summary>
    public static ProviderPaymentStatus MapOrderStatus(YandexPayOrderDetails order)
    {
        var status = order.PaymentStatus?.Trim().ToUpperInvariant() switch
        {
            "CAPTURED" => ProviderPaymentStatusType.Paid,
            "FAILED" => ProviderPaymentStatusType.Canceled,
            "REFUNDED" => ProviderPaymentStatusType.Refunded,
            "PARTIALLY_REFUNDED" => ProviderPaymentStatusType.Refunded,
            "VOIDED" => ProviderPaymentStatusType.Canceled,
            "PENDING" or "AUTHORIZED" or "CONFIRMED" => ProviderPaymentStatusType.Pending,
            _ => ProviderPaymentStatusType.Unknown,
        };

        decimal? amount = null;
        string? currency = null;
        if (!string.IsNullOrWhiteSpace(order.OrderAmount) &&
            decimal.TryParse(order.OrderAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            amount = parsed;
        }
        if (!string.IsNullOrWhiteSpace(order.CurrencyCode))
        {
            currency = order.CurrencyCode;
        }

        return new ProviderPaymentStatus(status, amount, currency, null, null);
    }
}
