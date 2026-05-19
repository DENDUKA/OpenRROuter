namespace OpenRussiaRouter.WebApp.Services.ControlPlane;

public sealed record RouterNodeDetails(
    RouterNodeInfo Node,
    IReadOnlyList<RouterProviderQuotaInfo> Providers,
    IReadOnlyList<RouterIntegrationKeyInfo> Keys,
    string? ApiError);

public sealed record RouterProviderQuotaInfo(
    string ConnectionId,
    string Provider,
    string Name,
    string AuthType,
    bool IsActive,
    string? TestStatus,
    bool QuotaSupported,
    string? UsageJson,
    string? Error);

public sealed record RouterIntegrationKeyInfo(
    string Id,
    string Name,
    string KeyPreview,
    string MachineId,
    bool IsActive,
    string ManagedBy,
    string CreatedAt);
