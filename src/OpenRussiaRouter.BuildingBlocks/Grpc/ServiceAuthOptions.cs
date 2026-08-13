namespace OpenRussiaRouter.BuildingBlocks.Grpc;

/// <summary>
/// Configuration for the s2s (service-to-service) authentication interceptor.
///
/// The interceptor guards <c>mutating</c> gRPC methods. Read methods are intentionally
/// left open: they do not modify state and existing clients need them in their current shape.
///
/// Two layers of defence:
/// <list type="number">
///   <item>
///     <b>Caller identity</b>: the client sends <c>x-rwt-caller</c> metadata identifying
///     which logical caller it is (e.g. <c>apigateway</c>, <c>webappadmin</c>).
///   </item>
///   <item>
///     <b>Shared secret</b>: the same client sends <c>x-rwt-s2s-token</c> matching the
///     configured token for that caller id. The mapping <c>caller-id → token</c> lives in
///     <see cref="Tokens"/>; values are compared with a constant-time check.
///   </item>
/// </list>
///
/// <see cref="MethodPolicies"/> is a per-method allowlist of caller ids. The interceptor owns
/// the mutating-method list (via <see cref="ServiceAuthServerOptions.ProtectedMethods"/>),
/// so a missing policy for one of those methods fails closed.
/// Methods outside that code-owned list are treated as <b>read-only</b> (no s2s required).
/// </summary>
public sealed class ServiceAuthOptions
{
    /// <summary>Metadata key carrying the caller identity (e.g. <c>api-gateway</c>).</summary>
    public const string CallerMetadataKey = "x-rwt-caller";

    /// <summary>Metadata key carrying the shared secret for the declared caller.</summary>
    public const string TokenMetadataKey = "x-rwt-s2s-token";

    /// <summary>Caller-id → shared token. Caller ids not present here cannot authenticate.</summary>
    public Dictionary<string, string> Tokens { get; set; } = new();

    /// <summary>
    /// Method-name → list of allowed caller ids. A missing/empty policy for a protected method
    /// denies the call. Method-name is the gRPC method's bare name (e.g. <c>RecordUsage</c>),
    /// without the service prefix.
    /// </summary>
    public Dictionary<string, string[]> MethodPolicies { get; set; } = new();
}
