using OpenRussiaRouter.ControlPlane.Domain;

namespace OpenRussiaRouter.ControlPlane.Application;

/// <summary>
/// Storage abstraction for router nodes and leases.
/// </summary>
public interface IControlPlaneStore
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
    /// <returns>Router nodes.</returns>
    IReadOnlyCollection<RouterNode> ListNodes();

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
}
