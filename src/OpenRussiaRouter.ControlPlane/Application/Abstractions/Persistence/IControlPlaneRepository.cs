using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;

public interface IControlPlaneRepository
{
    Task UpsertNodeAsync(RouterNode node, CancellationToken ct = default);

    Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default);

    Task<RouterNode?> GetNodeAsync(string nodeId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<RouterNode> Items, bool HasMore)> ListNodesAsync(int limit, string? afterName = null, CancellationToken ct = default);

    Task AddLeaseAsync(RouterLease lease, CancellationToken ct = default);

    Task<RouterLease?> RemoveLeaseAsync(string leaseId, CancellationToken ct = default);

    Task<IReadOnlyCollection<RouterLease>> RemoveExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default);

    Task ReleaseNodesAsync(IReadOnlyCollection<string> nodeIds, CancellationToken ct = default);

    Task SaveIntegrationKeyMappingAsync(string apiKeyId, string nodeId, string routerKeyId, CancellationToken ct = default);

    Task<IReadOnlyList<(string NodeId, string RouterKeyId)>> GetIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default);

    Task RemoveIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default);
}
