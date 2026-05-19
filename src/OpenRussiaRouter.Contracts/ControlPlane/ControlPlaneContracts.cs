namespace OpenRussiaRouter.Contracts.ControlPlane;

public enum RouterNodeStatus
{
    Disabled = 0,
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3
}

public sealed record RouterNodeLease(
    string LeaseId,
    string NodeId,
    Uri EndpointUrl,
    string ControlToken);

public sealed record RouterNodeSummary(
    string Id,
    string Name,
    Uri EndpointUrl,
    RouterNodeStatus Status,
    int InFlightRequests,
    DateTimeOffset? LastCheckedAtUtc);

