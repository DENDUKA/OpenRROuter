using Grpc.Core;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.ApiGateway.ControlPlane;

/// <summary>
/// Gateway Control Plane client backed by gRPC.
/// </summary>
public sealed class GrpcControlPlaneGatewayClient(ControlGrpc.RouterControlPlaneClient controlClient)
    : IControlPlaneGatewayClient
{
    /// <inheritdoc />
    public async Task<GatewayRouterLease?> SelectRouterNodeAsync(
        string modelId,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await controlClient.SelectRouterNodeAsync(
                new SelectRouterNodeRequest
                {
                    ModelId = modelId,
                    RequestId = requestId
                },
                cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(response.ErrorCode) ||
                string.IsNullOrWhiteSpace(response.LeaseId) ||
                string.IsNullOrWhiteSpace(response.NodeId) ||
                !Uri.TryCreate(response.EndpointUrl, UriKind.Absolute, out var endpoint))
            {
                return null;
            }

            return new GatewayRouterLease(
                response.LeaseId,
                response.NodeId,
                endpoint,
                response.TokenSecretName);
        }
        catch (RpcException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseId))
        {
            return;
        }

        try
        {
            await controlClient.ReleaseRouterNodeAsync(
                new ReleaseRouterNodeRequest
                {
                    LeaseId = leaseId,
                    Status = status
                },
                cancellationToken: cancellationToken);
        }
        catch (RpcException) when (!cancellationToken.IsCancellationRequested)
        {
            // Releasing a lease is best-effort; Control Plane TTL cleanup is the fallback.
        }
    }
}

