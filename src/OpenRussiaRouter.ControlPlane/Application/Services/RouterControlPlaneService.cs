using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class RouterControlPlaneService(IControlPlaneRepository repository)
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

    public async Task<RouterNodeLease> SelectRouterNodeAsync(string requestId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Value cannot be empty.", nameof(requestId));

        await ReleaseExpiredLeasesAsync(nowUtc, ct).ConfigureAwait(false);

        var node = (await repository.ListNodesAsync(MaxNodePageSize, null, ct).ConfigureAwait(false))
            .Items
            .Where(candidate => candidate.Enabled && candidate.Status == RouterNodeStatus.Healthy)
            .OrderBy(candidate => (decimal)candidate.InFlightRequests / candidate.CapacityWeight)
            .ThenBy(candidate => candidate.InFlightRequests)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (node is null)
            throw new InvalidOperationException("No healthy router nodes are available.");

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

    public async Task<IReadOnlyCollection<RouterNodeSummary>> ListRouterNodesAsync(CancellationToken ct = default)
    {
        return (await repository.ListNodesAsync(MaxNodePageSize, null, ct).ConfigureAwait(false))
            .Items
            .Select(node => new RouterNodeSummary(
                node.Id,
                node.Name,
                node.EndpointUrl,
                node.Enabled ? node.Status : RouterNodeStatus.Disabled,
                node.InFlightRequests,
                node.LastCheckedAtUtc))
            .ToArray();
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
