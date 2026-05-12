using OpenRussiaRouter.Contracts.ControlPlane;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

/// <summary>
/// Persisted router node row.
/// </summary>
public sealed class RouterNodeEntity
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string EndpointUrl { get; set; } = string.Empty;

    public string TokenSecretName { get; set; } = string.Empty;

    public int CapacityWeight { get; set; }

    public bool Enabled { get; set; }

    public RouterNodeStatus Status { get; set; }

    public int InFlightRequests { get; set; }

    public DateTimeOffset? LastCheckedAtUtc { get; set; }

    public int? LastHealthLatencyMs { get; set; }

    public string? LastHealthErrorCode { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Persisted router lease row.
/// </summary>
public sealed class RouterLeaseEntity
{
    public string Id { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
