using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenRussiaRouter.WebApp.Services.Payments.YandexPay;

/// <summary>
/// HTTP client for the Yandex Pay sandbox Merchant API.
/// Uses <c>Authorization: Api-Key {merchantId}</c> per Yandex Pay sandbox docs.
/// </summary>
public sealed class YandexPayClient
{
    private const string SandboxBaseUrl = "https://sandbox.pay.yandex.ru";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly YandexPaySettings _settings;
    private readonly ILogger<YandexPayClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public YandexPayClient(
        IHttpClientFactory httpClientFactory,
        Microsoft.Extensions.Options.IOptions<PaymentSettings> options,
        ILogger<YandexPayClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value.YandexPay;
        _logger = logger;
    }

    /// <summary>
    /// Create an order at Yandex Pay sandbox. Returns the URL the payer must be sent to.
    /// </summary>
    public async Task<string> CreateOrderAsync(
        string orderId,
        decimal amount,
        string successRedirectUrl,
        string failRedirectUrl,
        int ttlSeconds,
        CancellationToken ct)
    {
        var client = CreateHttpClient();

        var request = new YandexPayCreateOrderRequest
        {
            OrderId = orderId,
            CurrencyCode = "RUB",
            AvailablePaymentMethods = ["CARD"],
            OrderSource = "WEBSITE",
            Ttl = ttlSeconds,
            Purpose = "Top-up",
            Cart = new YandexPayCart
            {
                Items =
                [
                    new YandexPayCartItem
                    {
                        ProductId = $"topup-{orderId}",
                        Title = "Пополнение баланса Rawrter",
                        Quantity = new YandexPayItemQuantity { Count = "1" },
                        Total = amount.ToString("F2", CultureInfo.InvariantCulture),
                        Description = "Пополнение баланса",
                    }
                ],
                Total = new YandexPayCartTotal
                {
                    Amount = amount.ToString("F2", CultureInfo.InvariantCulture),
                },
            },
            RedirectUrls = new YandexPayRedirectUrls
            {
                OnSuccess = successRedirectUrl,
                OnAbort = failRedirectUrl,
                OnError = failRedirectUrl,
            },
        };

        _logger.LogInformation(
            "Yandex Pay: creating order {OrderId} for {Amount} RUB (ttl={Ttl}s).",
            orderId, amount, ttlSeconds);

        var response = await client.PostAsJsonAsync(
            $"{SandboxBaseUrl}/api/merchant/v1/orders", request, JsonOptions, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Yandex Pay: create order {OrderId} returned {StatusCode}: {Body}",
                orderId, response.StatusCode, body);
            throw new InvalidOperationException(
                $"Yandex Pay create order failed ({response.StatusCode}): {body}");
        }

        var result = JsonSerializer.Deserialize<YandexPayCreateOrderResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(result?.Data?.PaymentUrl))
        {
            // Include the body: a 200 that carries no link means the response shape drifted, and
            // without it the failure is indistinguishable from a rejected order.
            throw new InvalidOperationException(
                $"Yandex Pay create order returned no paymentUrl: {body}");
        }

        _logger.LogInformation(
            "Yandex Pay: order {OrderId} created, paymentUrl received.",
            orderId);

        return result.Data.PaymentUrl;
    }

    /// <summary>
    /// Get order details from Yandex Pay sandbox. Used for reconciliation and webhook amount verification.
    /// </summary>
    public async Task<YandexPayOrderDetails> GetOrderAsync(string orderId, CancellationToken ct)
    {
        var client = CreateHttpClient();

        _logger.LogDebug("Yandex Pay: fetching order {OrderId}.", orderId);

        var response = await client.GetAsync(
            $"{SandboxBaseUrl}/api/merchant/v1/orders/{orderId}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Yandex Pay: get order {OrderId} returned {StatusCode}: {Body}",
                orderId, response.StatusCode, body);
            throw new InvalidOperationException(
                $"Yandex Pay get order failed ({response.StatusCode}): {body}");
        }

        var result = JsonSerializer.Deserialize<YandexPayOrderResponse>(body, JsonOptions);
        if (result?.Data?.Order is null)
        {
            throw new InvalidOperationException($"Yandex Pay get order returned no data for {orderId}.");
        }

        return result.Data.Order;
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient("YandexPay");
        // In sandbox, the merchant id serves as the API key
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Api-Key", _settings.MerchantId);
        return client;
    }
}
