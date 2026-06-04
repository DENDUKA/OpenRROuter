namespace OpenRussiaRouter.ApiGateway.ControlPlane;

public interface IControlPlaneProvider
{

    Task<GatewayRouterLease?> SelectRouterNodeAsync(
        string modelId,
        string requestId,
        string? preferredNodeId,
        string? preferredNodeControlToken,
        IReadOnlyCollection<string>? excludedNodeIds,
        CancellationToken cancellationToken);

    Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken cancellationToken);
}

