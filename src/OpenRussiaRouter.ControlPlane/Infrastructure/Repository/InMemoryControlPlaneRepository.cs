using System.Collections.Concurrent;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Repository;

public sealed class InMemoryControlPlaneRepository : IControlPlaneRepository
{
    private readonly ConcurrentDictionary<string, RouterLease> _leases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RouterNode> _nodes = new(StringComparer.Ordinal);
    // key: "apiKeyId|nodeId"
    private readonly ConcurrentDictionary<string, (string NodeId, string RouterKeyId)> _keyMappings = new(StringComparer.Ordinal);

    public Task UpsertNodeAsync(RouterNode node, CancellationToken ct = default)
    {
        _nodes[node.Id] = node;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
    {
        if (!_nodes.TryRemove(nodeId, out _))
            return Task.FromResult(false);

        var nodeLeases = _leases.Values.Where(l => l.NodeId == nodeId).ToArray();
        foreach (var lease in nodeLeases)
            _leases.TryRemove(lease.Id, out _);

        return Task.FromResult(true);
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

    public Task AddLeaseAsync(RouterLease lease, CancellationToken ct = default)
    {
        _leases[lease.Id] = lease;
        return Task.CompletedTask;
    }

    public Task<RouterLease?> RemoveLeaseAsync(string leaseId, CancellationToken ct = default) =>
        Task.FromResult(_leases.TryRemove(leaseId, out var lease) ? lease : null);

    public Task<IReadOnlyCollection<RouterLease>> RemoveExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var expired = _leases.Values.Where(lease => lease.ExpiresAtUtc <= nowUtc).ToArray();
        foreach (var lease in expired)
            _leases.TryRemove(lease.Id, out _);

        return Task.FromResult<IReadOnlyCollection<RouterLease>>(expired);
    }

    public Task ReleaseNodesAsync(IReadOnlyCollection<string> nodeIds, CancellationToken ct = default)
    {
        foreach (var nodeId in nodeIds)
        {
            if (_nodes.TryGetValue(nodeId, out var node))
                node.Release();
        }

        return Task.CompletedTask;
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
