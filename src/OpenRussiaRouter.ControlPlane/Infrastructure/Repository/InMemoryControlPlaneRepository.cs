using System.Collections.Concurrent;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Repository;

public sealed class InMemoryControlPlaneRepository : IControlPlaneRepository
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, RouterLease> _leases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RouterNode> _nodes = new(StringComparer.Ordinal);
    // key: "apiKeyId|nodeId"
    private readonly ConcurrentDictionary<string, (string NodeId, string RouterKeyId)> _keyMappings = new(StringComparer.Ordinal);

    public Task UpsertNodeAsync(RouterNode node, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _nodes[node.Id] = node;
        }
        return Task.CompletedTask;
    }

    public Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_nodes.TryRemove(nodeId, out _))
                return Task.FromResult(false);

            var nodeLeases = _leases.Values.Where(l => l.NodeId == nodeId).ToArray();
            foreach (var lease in nodeLeases)
                _leases.TryRemove(lease.Id, out _);

            return Task.FromResult(true);
        }
    }

    public Task<RouterNode?> GetNodeAsync(string nodeId, CancellationToken ct = default) =>
        Task.FromResult(_nodes.GetValueOrDefault(nodeId));

    public Task<(IReadOnlyCollection<RouterNode> Items, bool HasMore)> ListNodesAsync(int limit, string? afterName = null, CancellationToken ct = default)
    {
        var ordered = _nodes.Values
            .OrderBy(node => node.Name)
            .AsEnumerable();

        if (afterName != null)
            ordered = ordered.Where(node => string.Compare(node.Name, afterName, StringComparison.Ordinal) > 0);

        var items = ordered.Take(limit + 1).ToArray();
        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return Task.FromResult<(IReadOnlyCollection<RouterNode>, bool)>((items, hasMore));
    }

    public Task<RouterNodeLease?> TryAcquireLeaseAsync(
        IReadOnlyCollection<string> candidateNodeIds,
        RouterLease lease,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            var candidateIds = new HashSet<string>(candidateNodeIds, StringComparer.Ordinal);
            var node = _nodes.Values
                .Where(candidate => candidateIds.Contains(candidate.Id) && candidate.Enabled && candidate.Status == RouterNodeStatus.Healthy)
                .OrderBy(candidate => (decimal)candidate.InFlightRequests / candidate.CapacityWeight)
                .ThenBy(candidate => candidate.InFlightRequests)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (node is null)
                return Task.FromResult<RouterNodeLease?>(null);

            node.Lease();
            _leases[lease.Id] = lease with { NodeId = node.Id };
            return Task.FromResult<RouterNodeLease?>(new RouterNodeLease(lease.Id, node.Id, node.EndpointUrl, node.ControlToken));
        }
    }

    public Task<bool> ReleaseLeaseAsync(string leaseId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_leases.TryRemove(leaseId, out var lease))
                return Task.FromResult(false);

            if (_nodes.TryGetValue(lease.NodeId, out var node))
                node.Release();
            return Task.FromResult(true);
        }
    }

    public Task<int> ReleaseExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var expired = _leases.Values.Where(lease => lease.ExpiresAtUtc <= nowUtc).ToArray();
            foreach (var group in expired.GroupBy(lease => lease.NodeId, StringComparer.Ordinal))
            {
                foreach (var lease in group)
                    _leases.TryRemove(lease.Id, out _);

                if (_nodes.TryGetValue(group.Key, out var node))
                {
                    foreach (var _ in group)
                        node.Release();
                }
            }

            return Task.FromResult(expired.Length);
        }
    }

    public Task<bool> ApplyHealthAsync(
        string nodeId,
        RouterNodeStatus status,
        DateTimeOffset checkedAtUtc,
        int? latencyMs,
        string? errorCode,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_nodes.TryGetValue(nodeId, out var node))
            {
                node.ApplyHealth(status, checkedAtUtc, latencyMs, errorCode);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }

    public Task SaveIntegrationKeyMappingAsync(string apiKeyId, string nodeId, string routerKeyId, CancellationToken ct = default)
    {
        _keyMappings[apiKeyId + "|" + nodeId] = (nodeId, routerKeyId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string NodeId, string RouterKeyId)>> GetIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default)
    {
        var prefix = apiKeyId + "|";
        IReadOnlyList<(string, string)> result = _keyMappings
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult(result);
    }

    public Task RemoveIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default)
    {
        var prefix = apiKeyId + "|";
        foreach (var key in _keyMappings.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            _keyMappings.TryRemove(key, out _);

        return Task.CompletedTask;
    }
}
