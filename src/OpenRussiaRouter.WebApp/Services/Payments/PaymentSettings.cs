namespace OpenRussiaRouter.WebApp.Services.Payments;

/// <summary>
/// Configuration for the payment subsystem. Bound from <c>Payments</c> section in appsettings.
/// WebApp owns this section — UI tasks (TS-007) must not touch it.
/// </summary>
public sealed class PaymentSettings
{
    public const string SectionName = "Payments";

    /// <summary>
    /// Active provider name (matches <c>IPaymentProvider.Name</c>). Empty in production base config;
    /// An empty value disables payments; <c>PaymentStartupGuard</c> fails fast if it equals
    /// <c>"mock"</c> or names an unknown provider in Production.
    /// </summary>
    public string ActiveProvider { get; set; } = string.Empty;

    /// <summary>
    /// UI hint for the minimum top-up amount. The authoritative min/max check lives in BillingService
    /// (TS-001) — this is purely UX so the form can disable the submit button on obviously-wrong values.
    /// </summary>
    public decimal MinTopUpUsd { get; set; } = 1.00m;

    /// <summary>UI hint for the maximum top-up amount. See <see cref="MinTopUpUsd"/>.</summary>
    public decimal MaxTopUpUsd { get; set; } = 10000.00m;

    /// <summary>Provider-specific knobs. For <c>mock</c> we read <c>SharedSecret</c>.</summary>
    public MockPaymentSettings Mock { get; set; } = new();

    /// <summary>Rollypay (СБП aggregator) knobs. Read by <c>RollypayPaymentProvider</c>.</summary>
    public RollypaySettings Rollypay { get; set; } = new();

    /// <summary>Robokassa knobs. Read by <c>RobokassaPaymentProvider</c>.</summary>
    public RobokassaSettings Robokassa { get; set; } = new();

    /// <summary>Yandex Pay sandbox knobs. Read by <c>YandexPayPaymentProvider</c>.</summary>
    public YandexPaySettings YandexPay { get; set; } = new();

    /// <summary>Reconciliation worker tuning. Defaults are conservative for dev.</summary>
    public ReconciliationSettings Reconciliation { get; set; } = new();
}

public sealed class MockPaymentSettings
{
    /// <summary>
    /// Shared secret expected in the <c>X-Mock-Signature</c> webhook header. Default <c>"dev"</c>
    /// is fine for local development; any non-empty value passes verification.
    /// </summary>
    public string SharedSecret { get; set; } = "dev";
}

/// <summary>
/// Configuration for the Rollypay СБП aggregator. Bound from <c>Payments:Rollypay</c>.
/// </summary>
/// <remarks>
/// <para>Rollypay has no sandbox — the only way to exercise the real flow is a live merchant
/// account with a minimal-amount payment. <see cref="ApiKey"/> and <see cref="SigningSecret"/>
/// are secrets and MUST come from the environment / a secret store, never from a committed
/// appsettings file.</para>
/// <para>Activation is gated by <c>Payments:ActiveProvider="rollypay"</c>. A missing
/// <see cref="ApiKey"/> causes outbound calls to throw, and a missing <see cref="SigningSecret"/>
/// causes every webhook to fail verification (HTTP 401) — both fail safe.</para>
/// </remarks>
public sealed class RollypaySettings
{
    /// <summary>API base URL. Defaults to the production host.</summary>
    public string BaseUrl { get; set; } = "https://rollypay.io";

    /// <summary>Касса API Key (UI → Интеграция). Sent as the <c>X-API-Key</c> header.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 signing secret (UI → Интеграция) used to verify inbound webhooks.</summary>
    public string SigningSecret { get; set; } = string.Empty;

    /// <summary>Optional redirect after a successful payment (Rollypay UI can also set a default).</summary>
    public string SuccessRedirectUrl { get; set; } = string.Empty;

    /// <summary>Optional redirect after a failed/abandoned payment.</summary>
    public string FailRedirectUrl { get; set; } = string.Empty;

    /// <summary>
    /// Send every payment as a Rollypay test payment (<c>test: true</c>). Test payments run on the
    /// SAME касса and the same credentials — there is no separate sandbox host or key.
    /// </summary>
    /// <remarks>
    /// <para>Rollypay forces a stub СБП method, never moves money, leaves the balance untouched and
    /// excludes the payment from its reports. The payer completes it from the pay page via a
    /// simulate button instead of a real QR. Callbacks still fire, carry <c>"test": true</c> plus an
    /// <c>X-Test-Mode: true</c> header, and are signed exactly like live ones.</para>
    /// <para>Because Billing credits real USD for a test payment just as it would for a live one,
    /// this MUST stay false in production — <c>PaymentStartupGuard</c> enforces that.</para>
    /// </remarks>
    public bool TestMode { get; set; }
}

/// <summary>
/// Configuration for Robokassa. Bound from <c>Payments:Robokassa</c>.
/// <see cref="Password1"/> is a secret: environment / secret store only.
/// </summary>
/// <remarks>
/// With <see cref="TestMode"/> Robokassa expects the TEST password pair from the shop's technical
/// settings. A test payment moves no money but Billing would still credit real balance, so
/// <c>PaymentStartupGuard</c> forbids it in Production.
/// </remarks>
public sealed class RobokassaSettings
{
    /// <summary>Shop identifier (МerchantLogin) from the shop's technical settings.</summary>
    public string MerchantLogin { get; set; } = string.Empty;

    /// <summary>Password #1: signs the outgoing payment link.</summary>
    public string Password1 { get; set; } = string.Empty;

    /// <summary>
    /// Public webhook URL (<c>https://&lt;domain&gt;/webhook/robokassa</c>) sent with every payment as
    /// <c>ResultUrl2</c>. The shop has no Password #2 / ResultURL, so this is how Robokassa reaches us;
    /// the notification is a JWS verified against Robokassa's certificate.
    /// </summary>
    public string NotifyUrl { get; set; } = string.Empty;

    /// <summary>Hash algorithm chosen in the shop settings: <c>MD5</c> (default) or <c>SHA256</c>.</summary>
    public string HashAlgorithm { get; set; } = "MD5";

    /// <summary>Payment page. Defaults to the production host.</summary>
    public string PayUrl { get; set; } = "https://auth.robokassa.ru/Merchant/Index.aspx";

    /// <summary>Send payments as test (<c>IsTest=1</c>). Must stay false in Production.</summary>
    public bool TestMode { get; set; }

    /// <summary>Attach the 54-ФЗ <c>Receipt</c> (needed for Робочеки fiscalisation).</summary>
    public bool ReceiptEnabled { get; set; } = true;

    /// <summary>Taxation system in the receipt: <c>usn_income</c> (УСН доходы), <c>usn_income_outcome</c>, <c>patent</c>, ...</summary>
    public string TaxationSystem { get; set; } = "usn_income";

    /// <summary>Receipt payment method. <c>full_payment</c> issues a single receipt at payment time.</summary>
    public string PaymentMethod { get; set; } = "full_payment";

    /// <summary>Receipt payment object: <c>service</c> for API access.</summary>
    public string PaymentObject { get; set; } = "service";

    /// <summary>Receipt VAT: <c>none</c> (without VAT) for a УСН sole proprietor.</summary>
    public string Tax { get; set; } = "none";

    /// <summary>Line name printed on the receipt.</summary>
    public string ReceiptItemName { get; set; } = "Пополнение баланса Rawrter";
}

/// <summary>
/// Configuration for Yandex Pay sandbox integration. Bound from <c>Payments:YandexPay</c>.
/// Disabled by default; sandbox is enabled only when <see cref="Enabled"/> and <see cref="Sandbox"/> are both true.
/// </summary>
public sealed class YandexPaySettings
{
    /// <summary>Master switch. When false, Yandex Pay SDK button and checkout endpoint are off.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Sandbox mode flag. When true, uses sandbox base URL and sandbox JWK endpoint.
    /// Must be true in production until the integration is fully verified.
    /// </summary>
    public bool Sandbox { get; set; }

    /// <summary>Yandex Pay merchant id. In sandbox mode this also serves as the API key.</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Redirect URL after a successful payment.</summary>
    public string SuccessRedirectUrl { get; set; } = string.Empty;

    /// <summary>Redirect URL after a failed/abandoned payment.</summary>
    public string FailRedirectUrl { get; set; } = string.Empty;

    /// <summary>Order time-to-live in seconds. Default 1800 (30 minutes). Min 120, max 604800.</summary>
    public int OrderTtlSeconds { get; set; } = 1800;

    /// <summary>How long to cache JWK keys before refresh. Default 10 minutes.</summary>
    public TimeSpan JwkCacheDuration { get; set; } = TimeSpan.FromMinutes(10);
}

public sealed class ReconciliationSettings
{
    /// <summary>Interval between reconciliation sweeps. Default 5 minutes.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long to lease a payment before another worker can pick it up. Default 2 minutes.</summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>Only pick <c>Created</c> payments older than this threshold. Default 10 minutes.</summary>
    public int OlderThanMinutes { get; set; } = 10;

    /// <summary>Max payments to acquire per cycle. Default 50.</summary>
    public int BatchLimit { get; set; } = 50;
}
