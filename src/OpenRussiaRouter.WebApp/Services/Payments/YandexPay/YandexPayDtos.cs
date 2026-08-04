using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenRussiaRouter.WebApp.Services.Payments.YandexPay;

/// <summary>
/// Request body for POST /api/merchant/v1/orders (Yandex Pay sandbox).
/// </summary>
public sealed class YandexPayCreateOrderRequest
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = "RUB";

    [JsonPropertyName("cart")]
    public YandexPayCart Cart { get; set; } = new();

    [JsonPropertyName("availablePaymentMethods")]
    public string[] AvailablePaymentMethods { get; set; } = ["CARD"];

    [JsonPropertyName("redirectUrls")]
    public YandexPayRedirectUrls RedirectUrls { get; set; } = new();

    [JsonPropertyName("orderSource")]
    public string? OrderSource { get; set; }

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 1800;

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    [JsonPropertyName("metadata")]
    public string? Metadata { get; set; }

    [JsonPropertyName("billingPhone")]
    public string? BillingPhone { get; set; }
}

public sealed class YandexPayCart
{
    [JsonPropertyName("items")]
    public YandexPayCartItem[] Items { get; set; } = [];

    [JsonPropertyName("total")]
    public YandexPayCartTotal Total { get; set; } = new();
}

public sealed class YandexPayCartItem
{
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public YandexPayItemQuantity Quantity { get; set; } = new();

    [JsonPropertyName("total")]
    public string Total { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class YandexPayItemQuantity
{
    [JsonPropertyName("count")]
    public string Count { get; set; } = "1";
}

public sealed class YandexPayCartTotal
{
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;
}

public sealed class YandexPayRedirectUrls
{
    [JsonPropertyName("onSuccess")]
    public string OnSuccess { get; set; } = string.Empty;

    [JsonPropertyName("onAbort")]
    public string? OnAbort { get; set; }

    [JsonPropertyName("onError")]
    public string? OnError { get; set; }
}

/// <summary>
/// Response from POST /api/merchant/v1/orders.
/// </summary>
public sealed class YandexPayCreateOrderResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("data")]
    public YandexPayOrderData? Data { get; set; }
}

public sealed class YandexPayOrderData
{
    [JsonPropertyName("order")]
    public YandexPayOrderDetails? Order { get; set; }
}

public sealed class YandexPayOrderDetails
{
    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("paymentUrl")]
    public string? PaymentUrl { get; set; }

    [JsonPropertyName("paymentStatus")]
    public string? PaymentStatus { get; set; }

    [JsonPropertyName("orderAmount")]
    public string? OrderAmount { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("created")]
    public DateTimeOffset? Created { get; set; }

    [JsonPropertyName("updated")]
    public DateTimeOffset? Updated { get; set; }
}

/// <summary>
/// Response from GET /api/merchant/v1/orders/{orderId}.
/// </summary>
public sealed class YandexPayOrderResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("data")]
    public YandexPayOrderData? Data { get; set; }
}

/// <summary>
/// Minimal webhook payload extracted from the decoded JWT claims.
/// We only need the fields from the "order" claim.
/// </summary>
public sealed class YandexPayWebhookPayload
{
    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("eventTime")]
    public string? EventTime { get; set; }

    [JsonPropertyName("merchantId")]
    public string? MerchantId { get; set; }

    [JsonPropertyName("order")]
    public YandexPayWebhookOrderData? Order { get; set; }

    [JsonPropertyName("operation")]
    public YandexPayWebhookOperationData? Operation { get; set; }
}

public sealed class YandexPayWebhookOrderData
{
    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("paymentStatus")]
    public string? PaymentStatus { get; set; }
}

public sealed class YandexPayWebhookOperationData
{
    [JsonPropertyName("operationId")]
    public string? OperationId { get; set; }

    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("operationType")]
    public string? OperationType { get; set; }
}

/// <summary>
/// Minimal JWK response from GET /api/jwks.
/// </summary>
public sealed class YandexPayJwksResponse
{
    [JsonPropertyName("keys")]
    public JsonElement[] Keys { get; set; } = [];
}
