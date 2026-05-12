namespace OpenRussiaRouter.Contracts.ControlPlane;

/// <summary>
/// Router node status used by Control Plane and admin dashboard.
/// </summary>
public enum RouterNodeStatus
{
    Disabled = 0,
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3
}

/// <summary>
/// Router node selected for one Gateway request.
/// </summary>
/// <param name="LeaseId">Lease id to release after request completion.</param>
/// <param name="NodeId">Router node id.</param>
/// <param name="EndpointUrl">Internal HTTPS endpoint.</param>
/// <param name="TokenSecretName">Secret name/reference used to resolve node token.</param>
public sealed record RouterNodeLease(
    string LeaseId,
    string NodeId,
    Uri EndpointUrl,
    string TokenSecretName);

/// <summary>
/// Read model for admin router node dashboard.
/// </summary>
/// <param name="Id">Node id.</param>
/// <param name="Name">Display name.</param>
/// <param name="EndpointUrl">Node HTTPS endpoint.</param>
/// <param name="Status">Current health status.</param>
/// <param name="InFlightRequests">Current in-flight request count.</param>
/// <param name="LastCheckedAtUtc">Last health check timestamp.</param>
public sealed record RouterNodeSummary(
    string Id,
    string Name,
    Uri EndpointUrl,
    RouterNodeStatus Status,
    int InFlightRequests,
    DateTimeOffset? LastCheckedAtUtc);

