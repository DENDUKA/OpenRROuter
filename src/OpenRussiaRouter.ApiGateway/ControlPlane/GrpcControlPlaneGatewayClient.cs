using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.ApiGateway.ControlPlane;

public sealed class GrpcControlPlaneGatewayClient(
    ControlGrpc.RouterControlPlaneClient controlClient,
    ILogger<GrpcControlPlaneGatewayClient> logger)
    : IControlPlaneGatewayClient
{

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
        catch (RpcException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Control Plane router selection failed. RequestId: {RequestId}; ModelId: {ModelId}; RpcStatus: {RpcStatus}", requestId, modelId, ex.StatusCode);
            return null;
        }
    }

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
        catch (RpcException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Control Plane lease release failed. LeaseId: {LeaseId}; Status: {Status}; RpcStatus: {RpcStatus}", leaseId, status, ex.StatusCode);
        }
    }
}

