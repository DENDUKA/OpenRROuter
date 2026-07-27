namespace OpenRussiaRouter.BuildingBlocks.Grpc;

/// <summary>
/// Server-side gRPC interceptor options. Contains the code-owned allowlist of mutating methods
/// that require s2s authentication. This set is specific to each service and must be configured
/// in the service's DI registration.
/// </summary>
public sealed class ServiceAuthServerOptions
{
    /// <summary>
    /// Code-owned allowlist of mutating methods enforced by the interceptor. Any method not
    /// listed here is treated as read-only and stays open. A method added here MUST also receive
    /// a <c>ServiceAuth:MethodPolicies</c> entry in every environment, otherwise the interceptor
    /// fails closed with <c>missing_method_policy</c>.
    /// </summary>
    public HashSet<string> ProtectedMethods { get; set; } = new(StringComparer.Ordinal);
}
