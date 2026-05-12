using Grpc.Core;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.ControlPlane.Application;

/// <summary>
/// gRPC Router Control Plane API used by Gateway.
/// </summary>
public sealed class GrpcRouterControlPlaneService(RouterControlPlaneService controlPlane)
    : ControlGrpc.RouterControlPlaneBase
{
    /// <inheritdoc />
    public override Task<SelectRouterNodeResponse> SelectRouterNode(
        SelectRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            var lease = controlPlane.SelectRouterNode(request.RequestId, DateTimeOffset.UtcNow);
            return Task.FromResult(new SelectRouterNodeResponse
            {
                LeaseId = lease.LeaseId,
                NodeId = lease.NodeId,
                EndpointUrl = lease.EndpointUrl.ToString(),
                TokenSecretName = lease.TokenSecretName
            });
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(new SelectRouterNodeResponse
            {
                ErrorCode = ErrorCodes.RouterUnavailable
            });
        }
    }

    /// <inheritdoc />
    public override Task<ReleaseRouterNodeResponse> ReleaseRouterNode(
        ReleaseRouterNodeRequest request,
        ServerCallContext context)
    {
        controlPlane.ReleaseRouterNode(request.LeaseId);
        return Task.FromResult(new ReleaseRouterNodeResponse { Accepted = true });
    }
}

