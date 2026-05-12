using Grpc.Core;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class GrpcRouterControlPlaneService(RouterControlPlaneService controlPlane)
    : ControlGrpc.RouterControlPlaneBase
{

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

    public override Task<ReleaseRouterNodeResponse> ReleaseRouterNode(
        ReleaseRouterNodeRequest request,
        ServerCallContext context)
    {
        controlPlane.ReleaseRouterNode(request.LeaseId);
        return Task.FromResult(new ReleaseRouterNodeResponse { Accepted = true });
    }

    public override Task<RegisterRouterNodeResponse> RegisterRouterNode(
        RegisterRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            var node = controlPlane.RegisterNode(
                request.Name,
                new Uri(request.EndpointUrl),
                request.TokenSecretName,
                request.CapacityWeight,
                DateTimeOffset.UtcNow);

            return Task.FromResult(new RegisterRouterNodeResponse
            {
                Registered = true,
                NodeId = node.Id
            });
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException)
        {
            return Task.FromResult(new RegisterRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<DisableRouterNodeResponse> DisableRouterNode(
        DisableRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            controlPlane.DisableNode(request.NodeId);
            return Task.FromResult(new DisableRouterNodeResponse { Disabled = true });
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult(new DisableRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<ListRouterNodesResponse> ListRouterNodes(
        ListRouterNodesRequest request,
        ServerCallContext context)
    {
        var response = new ListRouterNodesResponse();
        response.Nodes.AddRange(controlPlane.ListRouterNodes().Select(node => new Contracts.Grpc.ControlPlane.V1.RouterNodeSummary
        {
            NodeId = node.Id,
            Name = node.Name,
            EndpointUrl = node.EndpointUrl.ToString(),
            Status = node.Status.ToString(),
            InFlightRequests = node.InFlightRequests,
            LastCheckedAtUtc = node.LastCheckedAtUtc?.ToString("O") ?? string.Empty
        }));

        return Task.FromResult(response);
    }
}

