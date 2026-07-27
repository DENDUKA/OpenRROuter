using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using OpenRussiaRouter.ControlPlane.Domain;

namespace OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;

public interface IControlPlaneRepository
{
    Task UpsertNodeAsync(RouterNode node, CancellationToken ct = default);

    Task<bool> UpdateNodeMetadataAsync(
        string nodeId,
        string name,
        Uri endpointUrl,
        string controlToken,
        int capacityWeight,
        CancellationToken ct = default);

    Task<bool> UpdateNodeControlTokenAsync(string nodeId, string controlToken, CancellationToken ct = default);

    Task<bool> UpdateNodeEnabledAsync(string nodeId, bool enabled, CancellationToken ct = default);

    Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default);

    Task<RouterNode?> GetNodeAsync(string nodeId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<RouterNode> Items, bool HasMore)> ListNodesAsync(int limit, string? afterName = null, CancellationToken ct = default);

    Task<RouterNodeLease?> TryAcquireLeaseAsync(
        IReadOnlyCollection<string> candidateNodeIds,
        RouterLease lease,
        CancellationToken ct = default);

    Task<bool> ReleaseLeaseAsync(string leaseId, CancellationToken ct = default);

    Task<int> ReleaseExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default);

    Task<bool> ApplyHealthAsync(
        string nodeId,
        RouterNodeStatus status,
        DateTimeOffset checkedAtUtc,
        int? latencyMs,
        string? errorCode,
        CancellationToken ct = default);

    Task SaveIntegrationKeyMappingAsync(string apiKeyId, string nodeId, string routerKeyId, CancellationToken ct = default);

    Task<IReadOnlyList<(string NodeId, string RouterKeyId)>> GetIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default);

    Task RemoveIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default);
}
