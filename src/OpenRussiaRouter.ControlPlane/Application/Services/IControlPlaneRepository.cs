using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Storage abstraction for router nodes and leases.
/// </summary>
public interface IControlPlaneRepository
{
    /// <summary>
    /// Upserts a router node.
    /// </summary>
    /// <param name="node">Router node.</param>
    void UpsertNode(RouterNode node);

    /// <summary>
    /// Gets a router node by id.
    /// </summary>
    /// <param name="nodeId">Router node id.</param>
    /// <returns>Router node or null.</returns>
    RouterNode? GetNode(string nodeId);

    /// <summary>
    /// Lists router nodes.
    /// </summary>
    /// <param name="limit">Maximum number of nodes to return.</param>
    /// <param name="afterName">Cursor: return nodes after this name.</param>
    /// <returns>Tuple of nodes and whether more exist.</returns>
    (IReadOnlyCollection<RouterNode> Items, bool HasMore) ListNodes(int limit, string? afterName = null);

    /// <summary>
    /// Adds a lease.
    /// </summary>
    /// <param name="lease">Router lease.</param>
    void AddLease(RouterLease lease);

    /// <summary>
    /// Removes a lease.
    /// </summary>
    /// <param name="leaseId">Lease id.</param>
    /// <returns>Removed lease or null.</returns>
    RouterLease? RemoveLease(string leaseId);

    /// <summary>
    /// Removes expired leases.
    /// </summary>
    /// <param name="nowUtc">Current timestamp.</param>
    /// <returns>Expired leases.</returns>
    IReadOnlyCollection<RouterLease> RemoveExpiredLeases(DateTimeOffset nowUtc);

    /// <summary>
    /// Decrements in-flight request count for multiple nodes atomically.
    /// </summary>
    /// <param name="nodeIds">Node ids to release.</param>
    void ReleaseNodes(IReadOnlyCollection<string> nodeIds);
}