using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class RouterControlPlaneService(
    IControlPlaneRepository repository,
    NodeModelCapabilityStore capabilityStore,
    RouterNodeSnapshotStore? snapshotStore = null)
{
    private static readonly TimeSpan DefaultLeaseTtl = TimeSpan.FromMinutes(5);
    private const int MaxNodePageSize = 1000;
    private readonly RouterNodeSnapshotStore _snapshotStore = snapshotStore ?? new RouterNodeSnapshotStore();

    public async Task<RouterNode> RegisterNodeAsync(
        string name,
        Uri endpointUrl,
        string controlToken,
        int capacityWeight,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var node = new RouterNode(
            Guid.NewGuid().ToString("N"),
            name,
            endpointUrl,
            controlToken,
            capacityWeight,
            nowUtc);

        await repository.UpsertNodeAsync(node, ct).ConfigureAwait(false);
        _snapshotStore.Upsert(node);
        return node;
    }

    public async Task UpdateNodeControlTokenAsync(string nodeId, string controlToken, CancellationToken ct = default)
    {
        var node = await GetRequiredNodeAsync(nodeId, ct).ConfigureAwait(false);
        _ = new RouterNode(node.Id, node.Name, node.EndpointUrl, controlToken, node.CapacityWeight, node.CreatedAtUtc);
        if (!await repository.UpdateNodeControlTokenAsync(nodeId, controlToken, ct).ConfigureAwait(false))
            throw new KeyNotFoundException("Router node was not found.");
        await RefreshSnapshotAsync(nodeId, ct).ConfigureAwait(false);
    }

    public async Task UpdateNodeAsync(
        string nodeId,
        string name,
        Uri endpointUrl,
        string controlToken,
        int capacityWeight,
        CancellationToken ct = default)
    {
        var node = await GetRequiredNodeAsync(nodeId, ct).ConfigureAwait(false);
        _ = new RouterNode(node.Id, name, endpointUrl, controlToken, capacityWeight, node.CreatedAtUtc);
        if (!await repository.UpdateNodeMetadataAsync(nodeId, name, endpointUrl, controlToken, capacityWeight, ct).ConfigureAwait(false))
            throw new KeyNotFoundException("Router node was not found.");
        await RefreshSnapshotAsync(nodeId, ct).ConfigureAwait(false);
    }

    public async Task DisableNodeAsync(string nodeId, CancellationToken ct = default)
    {
        await GetRequiredNodeAsync(nodeId, ct).ConfigureAwait(false);
        if (!await repository.UpdateNodeEnabledAsync(nodeId, false, ct).ConfigureAwait(false))
            throw new KeyNotFoundException("Router node was not found.");
        await RefreshSnapshotAsync(nodeId, ct).ConfigureAwait(false);
    }

    public async Task EnableNodeAsync(string nodeId, CancellationToken ct = default)
    {
        await GetRequiredNodeAsync(nodeId, ct).ConfigureAwait(false);
        if (!await repository.UpdateNodeEnabledAsync(nodeId, true, ct).ConfigureAwait(false))
            throw new KeyNotFoundException("Router node was not found.");
        await RefreshSnapshotAsync(nodeId, ct).ConfigureAwait(false);
    }

    public async Task DeleteNodeAsync(string nodeId, CancellationToken ct = default)
    {
        if (!await repository.RemoveNodeAsync(nodeId, ct).ConfigureAwait(false))
            throw new KeyNotFoundException("Router node was not found.");
        _snapshotStore.Remove(nodeId);
    }

    public async Task ApplyHealthAsync(
        string nodeId,
        RouterNodeStatus status,
        DateTimeOffset checkedAtUtc,
        int? latencyMs = null,
        string? errorCode = null,
        CancellationToken ct = default)
    {
        if (!await repository.ApplyHealthAsync(nodeId, status, checkedAtUtc, latencyMs, errorCode, ct).ConfigureAwait(false))
            throw new KeyNotFoundException("Router node was not found.");

        if (await repository.GetNodeAsync(nodeId, ct).ConfigureAwait(false) is { } node)
            _snapshotStore.Upsert(node);
    }

    public async Task<RouterNodeLease> SelectRouterNodeAsync(
        string? modelId,
        string requestId,
        DateTimeOffset nowUtc,
        string? preferredNodeId = null,
        string? preferredNodeControlToken = null,
        IReadOnlyCollection<string>? excludedNodeIds = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Value cannot be empty.", nameof(requestId));

        var snapshot = _snapshotStore.GetSnapshot();
        var all = (snapshot.Count == 0
                ? (await repository.ListNodesAsync(MaxNodePageSize, null, ct).ConfigureAwait(false)).Items.Select(ToMetadata)
                : snapshot)
            .Where(candidate => candidate.Enabled && candidate.Status == RouterNodeStatus.Healthy)
            .ToList();

        // Filter by model capability when the store has been populated.
        // If the store is still empty (e.g. right after startup, before the first Quartz job run),
        // fall back to the old least-load behaviour so the first requests are not rejected.
        IEnumerable<RouterNodeRoutingMetadata> candidates = all;
        if (!string.IsNullOrWhiteSpace(modelId) && !capabilityStore.IsEmpty)
            candidates = all.Where(n => capabilityStore.NodeSupportsModel(n.Id, modelId));

        if (excludedNodeIds != null && excludedNodeIds.Count > 0)
        {
            var excludedSet = new HashSet<string>(excludedNodeIds, StringComparer.OrdinalIgnoreCase);
            candidates = candidates.Where(n => !excludedSet.Contains(n.Id));
        }

        if (!string.IsNullOrWhiteSpace(preferredNodeId))
        {
            var nodeId = preferredNodeId.Trim();
            var controlToken = preferredNodeControlToken?.Trim() ?? string.Empty;
            candidates = candidates.Where(n =>
                string.Equals(n.Id, nodeId, StringComparison.Ordinal) &&
                string.Equals(n.ControlToken, controlToken, StringComparison.Ordinal));
        }

        var candidateIds = candidates.Select(candidate => candidate.Id).ToArray();
        if (candidateIds.Length == 0)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(modelId)
                    ? "No healthy router nodes are available."
                    : $"No healthy router nodes are available for model '{modelId}'.");

        var lease = new RouterLease(
            Guid.NewGuid().ToString("N"),
            string.Empty,
            requestId.Trim(),
            nowUtc.Add(DefaultLeaseTtl),
            nowUtc);
        return await repository.TryAcquireLeaseAsync(candidateIds, lease, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(modelId)
                    ? "No healthy router nodes are available."
                    : $"No healthy router nodes are available for model '{modelId}'.");
    }

    public async Task ReleaseRouterNodeAsync(string leaseId, CancellationToken ct = default)
    {
        await repository.ReleaseLeaseAsync(leaseId, ct).ConfigureAwait(false);
    }

    public async Task<RouterNode?> GetRouterNodeAsync(string nodeId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        return await repository.GetNodeAsync(nodeId.Trim(), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<RouterNode>> ListNodesForHealthChecksAsync(CancellationToken ct = default)
    {
        return (await repository.ListNodesAsync(MaxNodePageSize, null, ct).ConfigureAwait(false)).Items;
    }

    public async Task ReleaseExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        await repository.ReleaseExpiredLeasesAsync(nowUtc, ct).ConfigureAwait(false);
    }

    private async Task<RouterNode> GetRequiredNodeAsync(string nodeId, CancellationToken ct)
    {
        return await repository.GetNodeAsync(nodeId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Router node was not found.");
    }

    private static RouterNodeRoutingMetadata ToMetadata(RouterNode node) => new(
        node.Id,
        node.Name,
        node.EndpointUrl,
        node.ControlToken,
        node.CapacityWeight,
        node.Enabled,
        node.Status);

    private async Task RefreshSnapshotAsync(string nodeId, CancellationToken ct)
    {
        if (await repository.GetNodeAsync(nodeId, ct).ConfigureAwait(false) is { } node)
            _snapshotStore.Upsert(node);
    }
}
