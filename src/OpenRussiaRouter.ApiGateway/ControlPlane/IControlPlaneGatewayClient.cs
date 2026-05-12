namespace OpenRussiaRouter.ApiGateway.ControlPlane;

public interface IControlPlaneGatewayClient
{

    Task<GatewayRouterLease?> SelectRouterNodeAsync(
        string modelId,
        string requestId,
        CancellationToken cancellationToken);

    Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken cancellationToken);
}

