namespace OpenRussiaRouter.ControlPlane.Domain.Entities;

public sealed record RouterLease(
    string Id,
    string NodeId,
    string RequestId,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);

