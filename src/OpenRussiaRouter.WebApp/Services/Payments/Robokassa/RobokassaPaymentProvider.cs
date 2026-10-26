using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Services.Payments.Robokassa;

/// <summary>
/// <see cref="IPaymentProvider"/> adapter for Robokassa (cards incl. foreign-issued, СБП, ...).
/// There is no "create payment" API: <see cref="CreateAsync"/> only builds a signed redirect URL, and
/// the money confirmation arrives as a form-encoded POST to <c>/webhook/robokassa</c> (ResultURL).
/// </summary>
/// <remarks>
/// <para>Robokassa's <c>InvId</c> must be a positive integer, while our order id is a Billing payment
/// id. The order id therefore travels in <c>Shp_orderid</c> and <c>InvId</c> is derived from it
/// deterministically, so the <c>OK{InvId}</c> acknowledgement can be rebuilt from the webhook alone.</para>
/// <para>Robokassa has no payment lookup we use, so <see cref="GetPaymentAsync"/> reports
/// <see cref="ProviderPaymentStatusType.Unknown"/> (reconciliation no-op); a missed ResultURL is
/// redelivered by Robokassa itself.
/// shortcut: no OpStateExt lookup, add it if payments get stuck in Created.</para>
/// </remarks>
public sealed class RobokassaPaymentProvider : IPaymentProvider
{
    private const string OrderIdParam = "Shp_orderid";
    private const string SettlementCurrencyCode = "RUB";

    private static readonly JsonSerializerOptions ReceiptJson = new() { PropertyNamingPolicy = null };

    private readonly IOptionsMonitor<PaymentSettings> _settings;
    private readonly ILogger<RobokassaPaymentProvider> _logger;

    public RobokassaPaymentProvider(
        IOptionsMonitor<PaymentSettings> settings,
        ILogger<RobokassaPaymentProvider> logger)
    {
        _settings = settings;
        _logger = logger;
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

        parts.Add(settings.Password1);
        parts.Add($"{OrderIdParam}={request.OrderId}");
        var signature = Hash(settings.HashAlgorithm, string.Join(':', parts));

        var query = new Dictionary<string, string?>
        {
            ["MerchantLogin"] = settings.MerchantLogin,
            ["OutSum"] = outSum,
            ["InvId"] = invId,
            ["Description"] = Truncate(request.Description, 100),
            ["SignatureValue"] = signature,
            [OrderIdParam] = request.OrderId,
            ["Culture"] = "ru",
        };
        if (receipt is not null) query["Receipt"] = receipt;
        if (settings.TestMode) query["IsTest"] = "1";

        var payUrl = QueryHelpers.AddQueryString(settings.PayUrl, query);

        _logger.LogInformation(
            "Robokassa payment link built: order={OrderId} invId={InvId} amount={Amount} test={TestMode}",
            request.OrderId, invId, outSum, settings.TestMode);

        return Task.FromResult(new PaymentCreated(request.OrderId, payUrl));
    }

    public WebhookVerificationResult Verify(string rawBody, IHeaderDictionary headers)
    {
        var settings = _settings.CurrentValue.Robokassa;
        if (string.IsNullOrWhiteSpace(settings.Password2))
        {
            _logger.LogWarning("Robokassa webhook rejected: Password2 is not configured.");
            return WebhookVerificationResult.Invalid;
        }

        var form = QueryHelpers.ParseQuery(rawBody);
        var outSum = form.TryGetValue("OutSum", out var o) ? o.ToString() : "";
        var invId = form.TryGetValue("InvId", out var i) ? i.ToString() : "";
        var received = form.TryGetValue("SignatureValue", out var s) ? s.ToString() : "";
        if (outSum.Length == 0 || invId.Length == 0 || received.Length == 0)
        {
            _logger.LogWarning("Robokassa webhook rejected: missing OutSum/InvId/SignatureValue.");
            return WebhookVerificationResult.Invalid;
        }

        // Shp_* params are signed in alphabetical order with their original casing and values.
        var custom = form
            .Where(p => p.Key.StartsWith("Shp_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}");

        var expected = Hash(settings.HashAlgorithm,
            string.Join(':', new[] { outSum, invId, settings.Password2 }.Concat(custom)));

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected.ToUpperInvariant()),
            Encoding.ASCII.GetBytes(received.ToUpperInvariant()));

        if (!ok)
        {
            _logger.LogWarning("Robokassa webhook signature mismatch (invId={InvId}).", invId);
            return WebhookVerificationResult.Invalid;
        }
        return WebhookVerificationResult.Valid;
    }

    public PaymentWebhookEvent Parse(string rawBody)
    {
        var form = QueryHelpers.ParseQuery(rawBody);
        var orderId = form.TryGetValue(OrderIdParam, out var o) ? o.ToString() : "";
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new InvalidOperationException($"Robokassa webhook is missing {OrderIdParam}.");
        }

        decimal? amount = decimal.TryParse(
            form.TryGetValue("OutSum", out var s) ? s.ToString() : null,
            NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

        // ResultURL fires only for successful payments, so every verified webhook is a "paid" event.
        return new PaymentWebhookEvent(
            ProviderPaymentId: orderId,
            OrderId: orderId,
            EventType: PaymentWebhookEventTypes.Paid,
            ConfirmedAmount: amount,
            ConfirmedCurrency: SettlementCurrencyCode,
            ProviderEventId: null,
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
