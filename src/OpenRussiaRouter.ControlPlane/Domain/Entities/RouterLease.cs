namespace OpenRussiaRouter.ControlPlane.Domain.Entities;

/// <summary>
/// Temporary lease for one Gateway request routed to a 9Router node.
/// </summary>
/// <param name="Id">Lease id.</param>
/// <param name="NodeId">Router node id.</param>
/// <param name="RequestId">Gateway request id.</param>
/// <param name="ExpiresAtUtc">Lease expiration timestamp.</param>
/// <param name="CreatedAtUtc">Creation timestamp.</param>
public sealed record RouterLease(
    string Id,
    string NodeId,
    string RequestId,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);

