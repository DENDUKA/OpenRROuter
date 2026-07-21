namespace OpenRussiaRouter.ContentService.Infrastructure.Auth;

/// <summary>
/// Configuration for the s2s (service-to-service) authentication interceptor.
///
/// The interceptor guards the <b>admin</b> gRPC surface of ContentService. Public read methods
/// (<c>ListTools</c>, <c>GetTool</c>, <c>GetToolLogo</c>, <c>GetToolImage</c>,
/// <c>ListToolCategories</c>) are intentionally open — they serve the public catalog and only
/// ever expose published rows.
///
/// Same two layers of defence as BillingService: a caller id in <c>x-orr-caller</c> and a shared
/// secret in <c>x-orr-s2s-token</c>, compared in constant time. <see cref="MethodPolicies"/> is a
/// per-method allowlist of caller ids; a protected method with no policy fails closed.
/// </summary>
public sealed class ServiceAuthOptions
{
    public const string CallerMetadataKey = "x-orr-caller";

    public const string TokenMetadataKey = "x-orr-s2s-token";

    /// <summary>Caller-id → shared token. Caller ids not present here cannot authenticate.</summary>
    public Dictionary<string, string> Tokens { get; set; } = new();

    /// <summary>Bare gRPC method name (e.g. <c>UpsertTool</c>) → allowed caller ids.</summary>
    public Dictionary<string, string[]> MethodPolicies { get; set; } = new();
}
