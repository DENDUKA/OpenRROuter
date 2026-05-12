namespace OpenRussiaRouter.ApiGateway.ControlPlane.Interfaces;

/// <summary>
/// Control Plane operations needed by Gateway.
/// </summary>
public interface IControlPlaneGatewayClient
{
    /// <summary>
    /// Selects a router node and creates a lease.
    /// </summary>
    /// <param name="modelId">Public model id.</param>
    /// <param name="requestId">Gateway request id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Router lease.</returns>
    Task<GatewayRouterLease?> SelectRouterNodeAsync(
        string modelId,
        string requestId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases a router lease.
    /// </summary>
    /// <param name="leaseId">Lease id.</param>
    /// <param name="status">Final request status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken cancellationToken);
}

