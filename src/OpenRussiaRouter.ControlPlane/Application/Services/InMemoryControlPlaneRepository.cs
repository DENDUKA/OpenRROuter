using System.Collections.Concurrent;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// In-memory Control Plane store used before PostgreSQL persistence is added.
/// </summary>
public sealed class InMemoryControlPlaneRepository : IControlPlaneRepository
{
    private readonly ConcurrentDictionary<string, RouterLease> _leases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RouterNode> _nodes = new(StringComparer.Ordinal);

    /// <summary>
    /// Upserts a router node.
    /// </summary>
    /// <param name="node">Router node.</param>
    public void UpsertNode(RouterNode node)
    {
        _nodes[node.Id] = node;
    }

    /// <summary>
    /// Gets a router node by id.
    /// </summary>
    /// <param name="nodeId">Router node id.</param>
    /// <returns>Router node or null.</returns>
    public RouterNode? GetNode(string nodeId) => _nodes.GetValueOrDefault(nodeId);

    /// <summary>
    /// Lists registered router nodes.
    /// </summary>
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

    /// <summary>
    /// Adds a lease.
    /// </summary>
    /// <param name="lease">Lease.</param>
    public void AddLease(RouterLease lease)
    {
        _leases[lease.Id] = lease;
    }

    /// <summary>
    /// Removes a lease.
    /// </summary>
    /// <param name="leaseId">Lease id.</param>
    /// <returns>Removed lease or null.</returns>
    public RouterLease? RemoveLease(string leaseId)
    {
        return _leases.TryRemove(leaseId, out var lease) ? lease : null;
    }

    /// <summary>
    /// Removes expired leases.
    /// </summary>
    /// <param name="nowUtc">Current timestamp.</param>
    /// <returns>Expired leases.</returns>
    public IReadOnlyCollection<RouterLease> RemoveExpiredLeases(DateTimeOffset nowUtc)
    {
        var expired = _leases.Values.Where(lease => lease.ExpiresAtUtc <= nowUtc).ToArray();
        foreach (var lease in expired)
        {
            _leases.TryRemove(lease.Id, out _);
        }

        return expired;
    }

    /// <inheritdoc />
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
