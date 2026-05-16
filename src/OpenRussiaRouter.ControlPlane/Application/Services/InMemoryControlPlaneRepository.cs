using System.Collections.Concurrent;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class InMemoryControlPlaneRepository : IControlPlaneRepository
{
    private readonly ConcurrentDictionary<string, RouterLease> _leases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RouterNode> _nodes = new(StringComparer.Ordinal);

    public void UpsertNode(RouterNode node)
    {
        _nodes[node.Id] = node;
    }

    public bool RemoveNode(string nodeId)
    {
        if (!_nodes.TryRemove(nodeId, out _))
            return false;

        var nodeLeases = _leases.Values.Where(l => l.NodeId == nodeId).ToArray();
        foreach (var lease in nodeLeases)
            _leases.TryRemove(lease.Id, out _);

        return true;
    }

    public RouterNode? GetNode(string nodeId) => _nodes.GetValueOrDefault(nodeId);

    public (IReadOnlyCollection<RouterNode> Items, bool HasMore) ListNodes(int limit, string? afterName = null)
    {
        var ordered = _nodes.Values
            .OrderBy(node => node.Name)
            .AsEnumerable();

        if (afterName != null)
        {
            ordered = ordered.Where(node => string.Compare(node.Name, afterName, StringComparison.Ordinal) > 0);
        }

        var items = ordered.Take(limit + 1).ToArray();
        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    public void AddLease(RouterLease lease)
    {
        _leases[lease.Id] = lease;
    }

    public RouterLease? RemoveLease(string leaseId)
    {
        return _leases.TryRemove(leaseId, out var lease) ? lease : null;
    }

    public IReadOnlyCollection<RouterLease> RemoveExpiredLeases(DateTimeOffset nowUtc)
    {
        var expired = _leases.Values.Where(lease => lease.ExpiresAtUtc <= nowUtc).ToArray();
        foreach (var lease in expired)
        {
            _leases.TryRemove(lease.Id, out _);
        }

        return expired;
    }

    public void ReleaseNodes(IReadOnlyCollection<string> nodeIds)
    {
        foreach (var nodeId in nodeIds)
        {
            if (_nodes.TryGetValue(nodeId, out var node))
            {
                node.Release();
            }
        }
    }
}
