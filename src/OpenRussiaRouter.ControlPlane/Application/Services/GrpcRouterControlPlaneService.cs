using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class GrpcRouterControlPlaneService(
    RouterControlPlaneService controlPlane,
    IRouterKeySyncService keySyncService,
    IRouterNodeProviderClient providerClient,
    ILogger<GrpcRouterControlPlaneService>? logger = null)
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
        catch (InvalidOperationException ex)
        {
            logger?.LogWarning(ex, "SelectRouterNode failed. RequestId: {RequestId}; ModelId: {ModelId}", request.RequestId, request.ModelId);
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

            _ = keySyncService.SyncToNodeAsync(node.Id, CancellationToken.None);

            return Task.FromResult(new RegisterRouterNodeResponse
            {
                Registered = true,
                NodeId = node.Id
            });
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException)
        {
            logger?.LogWarning(ex, "RegisterRouterNode failed. Name: {Name}; EndpointUrl: {EndpointUrl}", request.Name, request.EndpointUrl);
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
        catch (KeyNotFoundException ex)
        {
            logger?.LogWarning(ex, "DisableRouterNode failed. NodeId: {NodeId}", request.NodeId);
            return Task.FromResult(new DisableRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<DeleteRouterNodeResponse> DeleteRouterNode(
        DeleteRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            controlPlane.DeleteNode(request.NodeId);
            return Task.FromResult(new DeleteRouterNodeResponse { Deleted = true });
        }
        catch (KeyNotFoundException ex)
        {
            logger?.LogWarning(ex, "DeleteRouterNode failed. NodeId: {NodeId}", request.NodeId);
            return Task.FromResult(new DeleteRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<EnableRouterNodeResponse> EnableRouterNode(
        EnableRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            controlPlane.EnableNode(request.NodeId);
            return Task.FromResult(new EnableRouterNodeResponse { Enabled = true });
        }
        catch (KeyNotFoundException ex)
        {
            logger?.LogWarning(ex, "EnableRouterNode failed. NodeId: {NodeId}", request.NodeId);
            return Task.FromResult(new EnableRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<ListRouterNodesResponse> ListRouterNodes(
        ListRouterNodesRequest request,
        ServerCallContext context)
    {
        var response = new ListRouterNodesResponse();
        response.Nodes.AddRange(controlPlane.ListNodesForHealthChecks().Select(MapNodeSummary));

        return Task.FromResult(response);
    }

    public override async Task<GetRouterNodeDetailsResponse> GetRouterNodeDetails(
        GetRouterNodeDetailsRequest request,
        ServerCallContext context)
    {
        var cancellationToken = context?.CancellationToken ?? CancellationToken.None;
        var node = controlPlane.GetRouterNode(request.NodeId);
        if (node is null)
        {
            return new GetRouterNodeDetailsResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }

        var response = new GetRouterNodeDetailsResponse { Node = MapNodeSummary(node) };
        var apiErrors = new List<string>();

        try
        {
            var quota = await providerClient.GetProviderQuotaAsync(
                node,
                request.IncludeInactiveProviders,
                cancellationToken).ConfigureAwait(false);

            response.Providers.AddRange(quota.Providers.Select(provider => new RouterProviderQuotaSummary
            {
                ConnectionId = provider.ConnectionId,
                Provider = provider.Provider,
                Name = provider.Name,
                AuthType = provider.AuthType,
                IsActive = provider.IsActive,
                TestStatus = provider.TestStatus ?? string.Empty,
                QuotaSupported = provider.QuotaSupported,
                UsageJson = provider.Usage?.GetRawText() ?? string.Empty,
                Error = provider.Error ?? string.Empty
            }));
        }
        catch (Exception ex) when (
            ex is RouterNodeProviderException or InvalidOperationException or HttpRequestException ||
            ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "Failed to load provider quota for router node. NodeId: {NodeId}", node.Id);
            apiErrors.Add("Provider quota unavailable: " + ex.Message);
        }

        try
        {
            var keys = await providerClient.ListIntegrationKeysAsync(
                node,
                cancellationToken).ConfigureAwait(false);

            response.Keys.AddRange(keys.Keys.Select(key => new RouterIntegrationKeySummary
            {
                Id = key.Id,
                Name = key.Name,
                KeyPreview = key.KeyPreview,
                MachineId = key.MachineId,
                IsActive = key.IsActive,
                ManagedBy = key.ManagedBy,
                CreatedAt = key.CreatedAt
            }));
        }
        catch (Exception ex) when (
            ex is RouterNodeProviderException or InvalidOperationException or HttpRequestException ||
            ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "Failed to load integration keys for router node. NodeId: {NodeId}", node.Id);
            apiErrors.Add("Integration keys unavailable: " + ex.Message);
        }

        response.ApiError = string.Join(" ", apiErrors);
        return response;
    }

    public override async Task<PushUserIntegrationKeyResponse> PushUserIntegrationKey(
        PushUserIntegrationKeyRequest request,
        ServerCallContext context)
    {
        var nodes = controlPlane.ListNodesForHealthChecks()
            .Where(n => n.Enabled)
            .ToArray();
        var pushedCount = 0;

        foreach (var node in nodes)
        {
            try
            {
                await providerClient.CreateIntegrationKeyAsync(
                    node, request.Name, request.RawKey, context.CancellationToken)
                    .ConfigureAwait(false);
                pushedCount++;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Не удалось запушить integration key на ноду {NodeId}", node.Id);
            }
        }

        return new PushUserIntegrationKeyResponse { PushedCount = pushedCount };
    }

    private static Contracts.Grpc.ControlPlane.V1.RouterNodeSummary MapNodeSummary(RouterNode node)
    {
        return new Contracts.Grpc.ControlPlane.V1.RouterNodeSummary
        {
            NodeId = node.Id,
            Name = node.Name,
            EndpointUrl = node.EndpointUrl.ToString(),
            Status = (node.Enabled ? node.Status : RouterNodeStatus.Disabled).ToString(),
            InFlightRequests = node.InFlightRequests,
            LastCheckedAtUtc = node.LastCheckedAtUtc?.ToString("O") ?? string.Empty,
            TokenSecretName = node.TokenSecretName,
            CapacityWeight = node.CapacityWeight,
            LastHealthErrorCode = node.LastHealthErrorCode ?? string.Empty
        };
    }

}

