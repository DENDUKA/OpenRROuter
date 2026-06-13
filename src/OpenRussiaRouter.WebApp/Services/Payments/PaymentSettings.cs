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
