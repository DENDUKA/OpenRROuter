using System.Collections.Concurrent;
using OpenRussiaRouter.ControlPlane.Domain;

namespace OpenRussiaRouter.ControlPlane.Application;

/// <summary>
/// In-memory Control Plane store used before PostgreSQL persistence is added.
/// </summary>
public sealed class InMemoryControlPlaneStore : IControlPlaneStore
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
    /// <returns>Router nodes.</returns>
    public IReadOnlyCollection<RouterNode> ListNodes() => _nodes.Values.OrderBy(node => node.Name).ToArray();

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
}
