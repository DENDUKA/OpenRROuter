using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenRussiaRouter.WebApp.Services.Payments.Rollypay;

/// <summary>
/// Thin HTTP transport for the Rollypay REST API. There is no official .NET SDK, so this is a
/// hand-written client. It is intentionally stateless: the owning <see cref="RollypayPaymentProvider"/>
/// constructs one per call with a factory-managed <see cref="HttpClient"/> (so handler rotation
/// keeps working even though the provider itself is a singleton).
/// </summary>
/// <remarks>
/// Error model: transient failures (5xx / 429 / timeout / socket) surface as
/// <see cref="RollypayUnavailableException"/> (safe to retry); deterministic 4xx surface as
/// <see cref="RollypayApiException"/> (fix the request, don't retry). See <c>docs/rollypay-dotnet.md</c>.
/// </remarks>
public sealed class RollypayClient
{
    // Rollypay's API is snake_case. WhenWritingNull keeps optional fields (redirect URLs, metadata)
    // out of the request body unless set.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly RollypaySettings _settings;

    public RollypayClient(HttpClient httpClient, RollypaySettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<RollypayPayment> CreatePaymentAsync(CreateRollypayPaymentRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        ApplyAuthHeaders(httpRequest);

        return await SendAsync<RollypayPayment>(httpRequest, ct)
            ?? throw new RollypayApiException(HttpStatusCode.OK, string.Empty, "Empty response body from Rollypay create payment");
    }

    public async Task<RollypayPayment> GetPaymentAsync(string paymentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new ArgumentException("paymentId must not be empty", nameof(paymentId));
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/payments/{Uri.EscapeDataString(paymentId)}");
        ApplyAuthHeaders(httpRequest);

        return await SendAsync<RollypayPayment>(httpRequest, ct)
            ?? throw new RollypayApiException(HttpStatusCode.OK, string.Empty, "Empty response body from Rollypay get payment");
    }

    private void ApplyAuthHeaders(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException("Rollypay API key is not configured (Payments:Rollypay:ApiKey).");
        }

        request.Headers.TryAddWithoutValidation("X-API-Key", _settings.ApiKey);
        // X-Nonce MUST be a fresh UUID per request — Rollypay rejects reused nonces with 401
        // inside a 10-minute anti-replay window.
        request.Headers.TryAddWithoutValidation("X-Nonce", Guid.NewGuid().ToString("D"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken ct) where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new RollypayUnavailableException("Rollypay request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new RollypayUnavailableException("Rollypay request failed.", ex);
        }

        using var _ = response;
        var body = await response.Content.ReadAsStringAsync(ct);

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new RollypayUnavailableException(
                $"Rollypay returned {(int)response.StatusCode}: {Truncate(body, 200)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new RollypayApiException(
                response.StatusCode, body,
                $"Rollypay rejected request: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new RollypayApiException(response.StatusCode, body, "Failed to parse Rollypay response", ex);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

/// <summary>Request body for <c>POST /api/v1/payments</c>. <c>amount</c> is a string, not a number.</summary>
public sealed class CreateRollypayPaymentRequest
{
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("payment_currency")]
    public string PaymentCurrency { get; set; } = "RUB";

    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; set; } = "sbp";

    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("success_redirect_url")]
    public string? SuccessRedirectUrl { get; set; }

    [JsonPropertyName("fail_redirect_url")]
    public string? FailRedirectUrl { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>Payment resource returned by create / get.</summary>
public sealed class RollypayPayment
{
    [JsonPropertyName("payment_id")]
    public string PaymentId { get; set; } = string.Empty;

    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("pay_url")]
    public string? PayUrl { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("payment_currency")]
    public string? PaymentCurrency { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("paid_at")]
    public DateTimeOffset? PaidAt { get; set; }
}

/// <summary>Webhook payload shape (parsed only after a successful signature verification).</summary>
public sealed class RollypayWebhookPayload
{
    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("payment_id")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

/// <summary>Deterministic 4xx from Rollypay — do not retry, fix the request.</summary>
public sealed class RollypayApiException : Exception
{
    public RollypayApiException(HttpStatusCode statusCode, string body, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Body = body;
    }

    public HttpStatusCode StatusCode { get; }

    public string Body { get; }
}

/// <summary>Transient failure (5xx / 429 / timeout / socket) — safe to retry or surface as "temporarily unavailable".</summary>
public sealed class RollypayUnavailableException : Exception
{
    public RollypayUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
