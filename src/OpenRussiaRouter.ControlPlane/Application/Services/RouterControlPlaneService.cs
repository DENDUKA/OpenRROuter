using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class RouterControlPlaneService(
    IControlPlaneRepository repository,
    NodeModelCapabilityStore capabilityStore)
{
    private static readonly TimeSpan DefaultLeaseTtl = TimeSpan.FromMinutes(5);
    private const int MaxNodePageSize = 1000;

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
        return node;
    }

    public async Task UpdateNodeControlTokenAsync(string nodeId, string controlToken, CancellationToken ct = default)
    {
        var node = await GetRequiredNodeAsync(nodeId, ct).ConfigureAwait(false);
        node.UpdateControlToken(controlToken);
        await repository.UpsertNodeAsync(node, ct).ConfigureAwait(false);
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
        node.Update(name, endpointUrl, controlToken, capacityWeight);
        await repository.UpsertNodeAsync(node, ct).ConfigureAwait(false);
    }

    public async Task DisableNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var node = await GetRequiredNodeAsync(nodeId, ct).ConfigureAwait(false);
        node.Disable();
        await repository.UpsertNodeAsync(node, ct).ConfigureAwait(false);
    }

    public async Task EnableNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var node = await GetRequiredNodeAsync(nodeId, ct).ConfigureAwait(false);
        node.Enable();
        await repository.UpsertNodeAsync(node, ct).ConfigureAwait(false);
    }

    public async Task DeleteNodeAsync(string nodeId, CancellationToken ct = default)
    {
        if (!await repository.RemoveNodeAsync(nodeId, ct).ConfigureAwait(false))
            throw new KeyNotFoundException("Router node was not found.");
    }

    public async Task ApplyHealthAsync(
        string nodeId,
        RouterNodeStatus status,
        DateTimeOffset checkedAtUtc,
        int? latencyMs = null,
        string? errorCode = null,
        CancellationToken ct = default)
    {
        var node = await GetRequiredNodeAsync(nodeId, ct).ConfigureAwait(false);
        node.ApplyHealth(status, checkedAtUtc, latencyMs, errorCode);
        await repository.UpsertNodeAsync(node, ct).ConfigureAwait(false);
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

        await ReleaseExpiredLeasesAsync(nowUtc, ct).ConfigureAwait(false);

        var all = (await repository.ListNodesAsync(MaxNodePageSize, null, ct).ConfigureAwait(false))
            .Items
            .Where(candidate => candidate.Enabled && candidate.Status == RouterNodeStatus.Healthy)
            .ToList();

        // Filter by model capability when the store has been populated.
        // If the store is still empty (e.g. right after startup, before the first Quartz job run),
        // fall back to the old least-load behaviour so the first requests are not rejected.
        IEnumerable<RouterNode> candidates = all;
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

        var node = candidates
            .OrderBy(candidate => (decimal)candidate.InFlightRequests / candidate.CapacityWeight)
            .ThenBy(candidate => candidate.InFlightRequests)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (node is null)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(modelId)
                    ? "No healthy router nodes are available."
                    : $"No healthy router nodes are available for model '{modelId}'.");

        node.Lease();
        await repository.UpsertNodeAsync(node, ct).ConfigureAwait(false);
        var lease = new RouterLease(
            Guid.NewGuid().ToString("N"),
            node.Id,
            requestId.Trim(),
            nowUtc.Add(DefaultLeaseTtl),
            nowUtc);
        await repository.AddLeaseAsync(lease, ct).ConfigureAwait(false);

        return new RouterNodeLease(lease.Id, node.Id, node.EndpointUrl, node.ControlToken);
    }

    public async Task ReleaseRouterNodeAsync(string leaseId, CancellationToken ct = default)
    {
        var lease = await repository.RemoveLeaseAsync(leaseId, ct).ConfigureAwait(false);
        if (lease is null)
            return;

        if (await repository.GetNodeAsync(lease.NodeId, ct).ConfigureAwait(false) is { } node)
        {
            node.Release();
            await repository.UpsertNodeAsync(node, ct).ConfigureAwait(false);
        }
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
        var expiredLeases = await repository.RemoveExpiredLeasesAsync(nowUtc, ct).ConfigureAwait(false);
        if (expiredLeases.Count == 0)
            return;

        var nodeIds = expiredLeases.Select(l => l.NodeId).Distinct().ToArray();
        await repository.ReleaseNodesAsync(nodeIds, ct).ConfigureAwait(false);
    }

    private async Task<RouterNode> GetRequiredNodeAsync(string nodeId, CancellationToken ct)
    {
        return await repository.GetNodeAsync(nodeId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Router node was not found.");
    }
}
