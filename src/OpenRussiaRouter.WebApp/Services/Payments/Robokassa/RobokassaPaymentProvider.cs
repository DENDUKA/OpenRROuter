using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Services.Payments.Robokassa;

/// <summary>
/// <see cref="IPaymentProvider"/> adapter for Robokassa (cards incl. foreign-issued, СБП, ...).
/// There is no "create payment" API: <see cref="CreateAsync"/> only builds a signed redirect URL, and
/// the money confirmation arrives as a JWS POST to <c>/webhook/robokassa</c> (ResultUrl2).
/// </summary>
/// <remarks>
/// <para>The shop has no Password #2 and no ResultURL, so the classic form webhook is not used. Instead
/// every payment link carries <c>ResultUrl2</c> (signed with Password #1) pointing at
/// <see cref="RobokassaSettings.NotifyUrl"/> + <c>?order={orderId}</c>. The notification is a compact JWS
/// (RS256, Robokassa certificate) holding only <c>invId</c>/<c>incSum</c>/<c>state</c>, so the order id
/// comes from the query and is bound to the signed <c>invId</c> (<see cref="InvoiceId"/> is a hash of it).</para>
/// <para>Robokassa's <c>InvId</c> must be a positive integer, while our order id is a Billing payment id,
/// hence the deterministic derivation.</para>
/// <para>Robokassa has no payment lookup we use, so <see cref="GetPaymentAsync"/> reports
/// <see cref="ProviderPaymentStatusType.Unknown"/> (reconciliation no-op).
/// shortcut: no OpStateExt lookup, add it if payments get stuck in Created.</para>
/// <para>shortcut: the certificate below is pinned and expires 2027-06-16 (Robokassa publishes the
/// replacement at docs.robokassa.ru/media/files/jwtsign.cer); swap it before then.</para>
/// </remarks>
public sealed class RobokassaPaymentProvider : IPaymentProvider
{
    internal const string OrderQueryParam = "order";
    private const string SettlementCurrencyCode = "RUB";
    private const string PaidState = "OK";

    private static readonly JsonSerializerOptions ReceiptJson = new() { PropertyNamingPolicy = null };

    private static readonly byte[] JwsPublicKey = X509Certificate2.CreateFromPem(
        """
        -----BEGIN CERTIFICATE-----
        MIIExzCCA6+gAwIBAgITbQAAC9yiLFSIsMEcOwABAAAL3DANBgkqhkiG9w0BAQsF
        ADA9MQswCQYDVQQGEwJSVTEPMA0GA1UEBxMGTW9zY293MR0wGwYDVQQDExRSb2Jv
        a2Fzc2EgSXNzdWluZyBDQTAeFw0yNjA2MTYxMTU5MjBaFw0yNzA2MTYxMjA5MjBa
        MIGPMQswCQYDVQQGEwJSVTEPMA0GA1UECBMGTU9TQ09XMQ8wDQYDVQQHEwZNT1ND
        T1cxEjAQBgNVBAoTCVJPQk9LQVNTQTESMBAGA1UECxMJUk9CT0tBU1NBMRIwEAYD
        VQQDEwlST0JPS0FTU0ExIjAgBgkqhkiG9w0BCQEWE3N1cHBvcnRAcm9ib2thc2Eu
        cnUwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCa4HPVZvGMaWFoV05q
        btXIO5xU1SdKL6BhzHjGXuo5EtfJmrAiptIsomHOpMmwgve2eFGCQvNYzcv9cA+r
        nUpxZyqpsZjPAun648iky2oS+urtHeQesQ6TURMHym3L5Jj6yBeY5Zm6lA1J3MVa
        iQpMCGazOI5sPg2edWulPqq6MyC1nCHAx4CMKofX5vJzubF3pVR65Nd7BArxvIuc
        u3wnAwqf7YbwQ4RkWrPWK+Goj2i5bGDM/BfZJOw0RN4uqV3zWxqsWYyBCrUmNYPX
        sb2eYT61Posy0T/lWTXfZxKN7WNezKhVl3HjDJr+1xVzEQnUvuSyb4q82z/ybZnZ
        2FFxAgMBAAGjggFrMIIBZzAOBgNVHQ8BAf8EBAMCBPAwQQYDVR0lBDowOAYEKwYB
        BQYIKwYBBQUHAwUGCCsGAQUFBwMEBggrBgEFBQcDAwYIKwYBBQUHAwIGCCsGAQUF
        BwMBMB0GA1UdDgQWBBRfZiaO8QzVZtA4ClsUWKMQALZt9jAfBgNVHSMEGDAWgBSh
        gNJMrkv3drSETcF5OE8uhJYaJzBEBgNVHR8EPTA7MDmgN6A1hjNodHRwOi8vYXV0
        aC5yb2Jva2Fzc2EucnUvcGtpL1JvYm9rYXNzYUlzc3VpbmdDQS5jcmwwfgYIKwYB
        BQUHAQEEcjBwMD8GCCsGAQUFBzAChjNodHRwOi8vYXV0aC5yb2Jva2Fzc2EucnUv
        cGtpL1JvYm9rYXNzYUlzc3VpbmdDQS5jcnQwLQYIKwYBBQUHMAGGIWh0dHA6Ly9h
        dXRoLnJvYm9rYXNzYS5ydS9wa2kvb2NzcDAMBgNVHRMBAf8EAjAAMA0GCSqGSIb3
        DQEBCwUAA4IBAQA5HypPsTNLSgCUl7s6CvnmtT3HIPGgEBXH7O0FZqZLYuPYL/kj
        vddjDMoiPtplfWIsi7Y2z46EwqZYybNx+/+eAni69w6UlWp4Ul1BpDX+2QrJwQhg
        SPyJkK8+ZW20BfLVanPB9Dy3JvDD3F47IvW0HoYkNmkP7LnP+S64ku0kOU3UhKJc
        Mpjhr7kpje6i2YZQhnXTp/Noceyj+6J08gJdUVgqEJoPB33MiIM0zsueQnABdWIs
        Xp0N1/Jeei9yG1Hb1TOkUaVcrm4uqrUkfMkvKlZ0crVtaL7adidqSGxZChvTX/0P
        TrFXkyKA1ptiVgIsbxZuOeTxJ3lDXDghviqW
        -----END CERTIFICATE-----
        """).GetRSAPublicKey()!.ExportSubjectPublicKeyInfo();

    private readonly IOptionsMonitor<PaymentSettings> _settings;
    private readonly ILogger<RobokassaPaymentProvider> _logger;
    private readonly byte[] _jwsPublicKey;

    public RobokassaPaymentProvider(
        IOptionsMonitor<PaymentSettings> settings,
        ILogger<RobokassaPaymentProvider> logger)
        : this(settings, logger, JwsPublicKey)
    {
    }

    /// <summary>Tests sign with their own key; production always uses the pinned Robokassa one.</summary>
    internal RobokassaPaymentProvider(
        IOptionsMonitor<PaymentSettings> settings,
        ILogger<RobokassaPaymentProvider> logger,
        byte[] jwsPublicKey)
    {
        _settings = settings;
        _logger = logger;
        _jwsPublicKey = jwsPublicKey;
    }

    public string Name => "robokassa";

    /// <summary>The shop is priced in rubles; foreign cards are converted by the issuer.</summary>
    public string? SettlementCurrency => SettlementCurrencyCode;

    public Task<PaymentCreated> CreateAsync(PaymentRequest request, CancellationToken ct)
    {
        var settings = _settings.CurrentValue.Robokassa;
        if (string.IsNullOrWhiteSpace(settings.MerchantLogin) || string.IsNullOrWhiteSpace(settings.Password1))
        {
            throw new InvalidOperationException("Robokassa MerchantLogin/Password1 are not configured.");
        }
        if (string.IsNullOrWhiteSpace(settings.NotifyUrl))
        {
            // Without ResultUrl2 Robokassa never tells us about the payment, so the user would pay and not be credited.
            throw new InvalidOperationException("Robokassa NotifyUrl is not configured.");
        }
        if (!string.Equals(request.Currency, SettlementCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Robokassa shop settles in {SettlementCurrencyCode}, got '{request.Currency}'.");
        }

        var outSum = request.Amount.ToString("F2", CultureInfo.InvariantCulture);
        var invId = InvoiceId(request.OrderId).ToString(CultureInfo.InvariantCulture);
        var parts = new List<string> { settings.MerchantLogin, outSum, invId };

        string? receipt = null;
        if (settings.ReceiptEnabled)
        {
            // The signature covers the once-url-encoded receipt; the query then encodes it again.
            receipt = Uri.EscapeDataString(BuildReceiptJson(settings, request.Amount));
            parts.Add(receipt);
        }

        // ResultUrl2 is signed and sent url-encoded exactly once (unlike Receipt, which is double-encoded).
        var resultUrl2 = Uri.EscapeDataString($"{settings.NotifyUrl}?{OrderQueryParam}={request.OrderId}");
        parts.Add(resultUrl2);
        parts.Add(settings.Password1);
        var signature = Hash(settings.HashAlgorithm, string.Join(':', parts));

        var query = new Dictionary<string, string?>
        {
            ["MerchantLogin"] = settings.MerchantLogin,
            ["OutSum"] = outSum,
            ["InvId"] = invId,
            ["Description"] = Truncate(request.Description, 100),
            ["SignatureValue"] = signature,
            ["Culture"] = "ru",
        };
        if (receipt is not null) query["Receipt"] = receipt;
        if (settings.TestMode) query["IsTest"] = "1";

        var payUrl = QueryHelpers.AddQueryString(settings.PayUrl, query) + "&ResultUrl2=" + resultUrl2;

        _logger.LogInformation(
            "Robokassa payment link built: order={OrderId} invId={InvId} amount={Amount} test={TestMode}",
            request.OrderId, invId, outSum, settings.TestMode);

        return Task.FromResult(new PaymentCreated(request.OrderId, payUrl));
    }

    public WebhookVerificationResult Verify(string rawBody, IHeaderDictionary headers)
    {
        var jws = FindJws(rawBody);
        if (jws is null)
        {
            _logger.LogWarning(
                "Robokassa webhook rejected: body is not a compact JWS (length={Length}, head='{Head}').",
                rawBody.Length, Truncate(rawBody, 60));
            return WebhookVerificationResult.Invalid;
        }

        var parts = jws.Split('.');
        try
        {
            using var header = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "RS256")
            {
                _logger.LogWarning("Robokassa webhook rejected: unexpected JWS alg.");
                return WebhookVerificationResult.Invalid;
            }

            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(_jwsPublicKey, out _);
            var valid = rsa.VerifyData(
                Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
                WebEncoders.Base64UrlDecode(parts[2]),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            if (!valid) _logger.LogWarning("Robokassa webhook JWS signature mismatch.");
            return valid ? WebhookVerificationResult.Valid : WebhookVerificationResult.Invalid;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            _logger.LogWarning(ex, "Robokassa webhook rejected: malformed JWS.");
            return WebhookVerificationResult.Invalid;
        }
    }

    public PaymentWebhookEvent Parse(string rawBody) => Parse(rawBody, QueryCollection.Empty);

    public PaymentWebhookEvent Parse(string rawBody, IQueryCollection query)
    {
        var jws = FindJws(rawBody) ?? throw new InvalidOperationException("Robokassa webhook is not a JWS.");
        var orderId = query.TryGetValue(OrderQueryParam, out var o) ? o.ToString() : "";
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new InvalidOperationException($"Robokassa webhook URL is missing '{OrderQueryParam}'.");
        }

        using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(jws.Split('.')[1]));
        var data = payload.RootElement.GetProperty("data");
        string Field(string name) => data.TryGetProperty(name, out var v) ? v.ToString() : "";

        var shop = _settings.CurrentValue.Robokassa.MerchantLogin;
        if (!string.Equals(Field("shop"), shop, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Robokassa notification is for a different shop.");
        }

        // The query is unauthenticated; the signed invId proves it belongs to this very order.
        if (Field("invId") != InvoiceId(orderId).ToString(CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("Robokassa notification invId does not match the order.");
        }

        decimal? amount = decimal.TryParse(Field("incSum"), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d : null;
        var opKey = Field("opKey");

        return new PaymentWebhookEvent(
            ProviderPaymentId: orderId,
            OrderId: orderId,
            EventType: Field("state") == PaidState ? PaymentWebhookEventTypes.Paid : PaymentWebhookEventTypes.Created,
            ConfirmedAmount: amount,
            ConfirmedCurrency: SettlementCurrencyCode,
            ProviderEventId: opKey.Length == 0 ? null : opKey,
            Reason: null);
    }

    public string? WebhookAckText(PaymentWebhookEvent evt) =>
        string.IsNullOrWhiteSpace(evt.OrderId)
            ? null
            : "OK" + InvoiceId(evt.OrderId).ToString(CultureInfo.InvariantCulture);

    public Task<ProviderPaymentStatus> GetPaymentAsync(string providerPaymentId, CancellationToken ct) =>
        Task.FromResult(new ProviderPaymentStatus(ProviderPaymentStatusType.Unknown, null, null, null, null));

    /// <summary>Stable positive 63-bit invoice number derived from the order id.</summary>
    internal static long InvoiceId(string orderId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(orderId));
        var value = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
        return value == 0 ? 1 : value;
    }

    /// <summary>The compact JWS from the body, tolerating a bare token or a form/quoted wrapper.</summary>
    private static string? FindJws(string body)
    {
        var text = body.Trim().Trim('"');
        if (IsCompactJws(text)) return text;
        foreach (var value in QueryHelpers.ParseQuery(text).Values)
        {
            var candidate = value.ToString().Trim();
            if (IsCompactJws(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsCompactJws(string text) =>
        text.Split('.').Length == 3 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static string BuildReceiptJson(RobokassaSettings settings, decimal amount) =>
        JsonSerializer.Serialize(new
        {
            sno = settings.TaxationSystem,
            items = new[]
            {
                new
                {
                    name = settings.ReceiptItemName,
                    quantity = 1,
                    sum = amount,
                    payment_method = settings.PaymentMethod,
                    payment_object = settings.PaymentObject,
                    tax = settings.Tax,
                },
            },
        }, ReceiptJson);

    private static string Hash(string algorithm, string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        var hash = string.Equals(algorithm, "SHA256", StringComparison.OrdinalIgnoreCase)
            ? SHA256.HashData(data)
            : MD5.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
