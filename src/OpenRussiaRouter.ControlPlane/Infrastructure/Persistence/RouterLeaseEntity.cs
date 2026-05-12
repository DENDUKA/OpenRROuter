using OpenRussiaRouter.Contracts.ControlPlane;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

public sealed class RouterLeaseEntity
{
    public string Id { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}