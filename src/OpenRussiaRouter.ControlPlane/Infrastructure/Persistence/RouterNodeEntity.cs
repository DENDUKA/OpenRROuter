using OpenRussiaRouter.ControlPlane.Domain;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

public sealed class RouterNodeEntity
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string EndpointUrl { get; set; } = string.Empty;

    public string ControlToken { get; set; } = string.Empty;

    public int CapacityWeight { get; set; }

    public bool Enabled { get; set; }

    public RouterNodeStatus Status { get; set; }

    public int InFlightRequests { get; set; }

    public DateTimeOffset? LastCheckedAtUtc { get; set; }

    public int? LastHealthLatencyMs { get; set; }

    public string? LastHealthErrorCode { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}