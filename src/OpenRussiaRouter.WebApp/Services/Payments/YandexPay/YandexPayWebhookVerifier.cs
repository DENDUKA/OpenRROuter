using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenRussiaRouter.WebApp.Api;

namespace OpenRussiaRouter.WebApp.Services.Payments.YandexPay;

/// <summary>
/// Verifies Yandex Pay webhook JWTs using ES256 signature validation against cached JWK keys.
/// Implements <see cref="IYandexPayWebhookVerifier"/> — the boundary used by
/// <see cref="Api.YandexPayWebhookEndpoint"/>.
/// </summary>
/// <remarks>
/// Verification order:
/// <list type="number">
///   <item>Parse the compact JWT (header.payload.signature).</item>
///   <item>Read <c>kid</c> from the JWT header.</item>
///   <item>Fetch JWK keys from Yandex Pay, cached for bounded duration.</item>
///   <item>If <c>kid</c> not found, refresh JWK keys and retry once.</item>
///   <item>Validate signature with the matching public key + exp/iat.</item>
///   <item>Validate <c>merchantId</c> from payload matches the configured value.</item>
///   <item>For CAPTURED events, fetch order details from Yandex API to get amount/currency
///         (webhooks do not include confirmed amount).</item>
/// </list>
/// IP-based trust is NOT used.
/// </remarks>
public sealed class YandexPayWebhookVerifier : IYandexPayWebhookVerifier
{
    private readonly YandexPayJwkProvider _jwkProvider;
    private readonly YandexPayClient _yandexPayClient;
    private readonly YandexPaySettings _settings;
    private readonly ILogger<YandexPayWebhookVerifier> _logger;

    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public YandexPayWebhookVerifier(
        YandexPayJwkProvider jwkProvider,
        YandexPayClient yandexPayClient,
        Microsoft.Extensions.Options.IOptions<PaymentSettings> options,
        ILogger<YandexPayWebhookVerifier> logger)
    {
        _jwkProvider = jwkProvider;
        _yandexPayClient = yandexPayClient;
        _settings = options.Value.YandexPay;
        _logger = logger;
    }

    /// <summary>
    /// Verify the raw compact JWT and return a normalized <see cref="YandexPayWebhookNotification"/>,
    /// or null if verification fails.
    /// </summary>
    public async Task<YandexPayWebhookNotification?> VerifyAsync(string rawJwt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawJwt))
        {
            _logger.LogWarning("Yandex Pay webhook: empty body.");
            return null;
        }

        // Split into parts to extract header and payload without validation
        var parts = rawJwt.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            _logger.LogWarning("Yandex Pay webhook: invalid JWT format ({PartCount} parts).", parts.Length);
            return null;
        }

        // Decode header to get kid
        string headerJson;
        try
        {
            headerJson = Base64UrlDecode(parts[0]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yandex Pay webhook: failed to decode JWT header.");
            return null;
        }

        string? kid;
        try
        {
            var headerDoc = JsonDocument.Parse(headerJson);
            kid = headerDoc.RootElement.TryGetProperty("kid", out var kidProp) ? kidProp.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yandex Pay webhook: failed to parse JWT header.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(kid))
        {
            _logger.LogWarning("Yandex Pay webhook: JWT header missing 'kid'.");
            return null;
        }

        // Fetch JWK keys
        IReadOnlyCollection<SecurityKey> keys;
        try
        {
            keys = await _jwkProvider.GetKeysAsync(kid, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yandex Pay webhook: failed to fetch JWK keys.");
            return null;
        }

        // Set up validation
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RequireSignedTokens = true,
            ValidAlgorithms = ["ES256"],
            IssuerSigningKeys = keys,
        };

        // Validate token — this verifies signature + exp
        ClaimsPrincipal principal;
        try
        {
            principal = TokenHandler.ValidateToken(rawJwt, validationParams, out _);
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Yandex Pay webhook: JWT token has expired.");
            return null;
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // Unknown kid — try refreshing keys and retry once
            _logger.LogWarning("JWT signature key not found for kid '{Kid}', refreshing JWK cache.", kid);
            try
            {
                keys = await _jwkProvider.GetKeysAsync(kid, ct);
                validationParams.IssuerSigningKeys = keys;
                principal = TokenHandler.ValidateToken(rawJwt, validationParams, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Yandex Pay webhook: JWT verification failed after JWK refresh for kid '{Kid}'.", kid);
                return null;
            }
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Yandex Pay webhook: invalid token.");
            return null;
        }

        // Validate merchantId from payload
        var merchantIdClaim = principal.FindFirst("merchantId")?.Value;
        if (!string.Equals(merchantIdClaim, _settings.MerchantId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Yandex Pay webhook: merchantId mismatch (expected '{Expected}', got '{Actual}').",
                _settings.MerchantId, merchantIdClaim);
            return null;
        }

        // Decode the payload directly from the raw JWT (the payload IS the event JSON)
        YandexPayWebhookPayload payload;
        try
        {
            var payloadJson = Base64UrlDecode(parts[1]);
            payload = JsonSerializer.Deserialize<YandexPayWebhookPayload>(payloadJson)
                ?? throw new InvalidOperationException("Deserialized to null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yandex Pay webhook: failed to deserialize payload.");
            return null;
        }

        // Map to notification
        var notification = await MapToNotificationAsync(payload, ct);
        return notification;
    }

    private async Task<YandexPayWebhookNotification?> MapToNotificationAsync(
        YandexPayWebhookPayload payload, CancellationToken ct)
    {
        var status = payload.Order?.PaymentStatus ?? payload.Operation?.Status;
        if (string.IsNullOrWhiteSpace(status))
        {
            _logger.LogWarning("Yandex Pay webhook: no status in payload.");
            return null;
        }

        var orderId = payload.Order?.OrderId ?? payload.Operation?.OrderId;
        var operationId = payload.Operation?.OperationId;

        decimal? amount = null;
        string? currency = null;

        // CAPTURED events do not include confirmed amount in the webhook — fetch from API
        if (string.Equals(status, "CAPTURED", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(orderId))
        {
            try
            {
                var orderDetails = await _yandexPayClient.GetOrderAsync(orderId, ct);
                if (!string.IsNullOrWhiteSpace(orderDetails.OrderAmount) &&
                    decimal.TryParse(orderDetails.OrderAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                {
                    amount = parsed;
                }
                currency = orderDetails.CurrencyCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Yandex Pay webhook: failed to fetch order details for {OrderId}, proceeding without amount.",
                    orderId);
            }
        }

        return new YandexPayWebhookNotification(
            ProviderPaymentId: orderId,
            OrderId: orderId,
            Status: status,
            Amount: amount,
            Currency: currency,
            EventId: operationId,
            Reason: payload.Operation?.OperationType);
    }

    private static string Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        var bytes = Convert.FromBase64String(output);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
