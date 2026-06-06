using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Grpc;

public sealed class GrpcRouterControlPlaneService(
    RouterControlPlaneService controlPlane,
    IControlPlaneRepository repository,
    IRouterNodeProvider providerClient,
    AuthGrpc.AuthServiceClient authClient,
    ILogger<GrpcRouterControlPlaneService> logger)
    : ControlGrpc.RouterControlPlaneBase
{
    private static readonly TimeSpan NodeDetailsProviderTimeout = TimeSpan.FromSeconds(3);

    public override async Task<SelectRouterNodeResponse> SelectRouterNode(
        SelectRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            var lease = await controlPlane.SelectRouterNodeAsync(
                request.ModelId,
                request.RequestId,
                DateTimeOffset.UtcNow,
                request.PreferredNodeId,
                request.PreferredNodeControlToken,
                request.ExcludedNodeIds,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new SelectRouterNodeResponse
            {
                LeaseId = lease.LeaseId,
                NodeId = lease.NodeId,
                EndpointUrl = lease.EndpointUrl.ToString(),
                ControlToken = lease.ControlToken
            };
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "SelectRouterNode failed. RequestId: {RequestId}; ModelId: {ModelId}", request.RequestId, request.ModelId);
            return new SelectRouterNodeResponse { ErrorCode = ErrorCodes.RouterUnavailable };
        }
    }

    public override async Task<ReleaseRouterNodeResponse> ReleaseRouterNode(
        ReleaseRouterNodeRequest request,
        ServerCallContext context)
    {
        await controlPlane.ReleaseRouterNodeAsync(request.LeaseId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        return new ReleaseRouterNodeResponse { Accepted = true };
    }

    public override async Task<RegisterRouterNodeResponse> RegisterRouterNode(
        RegisterRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            var node = await controlPlane.RegisterNodeAsync(
                request.Name,
                new Uri(request.EndpointUrl),
                request.ControlToken,
                request.CapacityWeight,
                DateTimeOffset.UtcNow,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new RegisterRouterNodeResponse
            {
                Registered = true,
                NodeId = node.Id
            };
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException)
        {
            logger.LogWarning(ex, "RegisterRouterNode failed. Name: {Name}; EndpointUrl: {EndpointUrl}", request.Name, request.EndpointUrl);
            return new RegisterRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<DisableRouterNodeResponse> DisableRouterNode(
        DisableRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            await controlPlane.DisableNodeAsync(request.NodeId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new DisableRouterNodeResponse { Disabled = true };
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex, "DisableRouterNode failed. NodeId: {NodeId}", request.NodeId);
            return new DisableRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<UpdateRouterNodeResponse> UpdateRouterNode(
        UpdateRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            await controlPlane.UpdateNodeAsync(
                request.NodeId,
                request.Name,
                new Uri(request.EndpointUrl),
                request.ControlToken,
                request.CapacityWeight,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new UpdateRouterNodeResponse { Updated = true };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or UriFormatException)
        {
            logger.LogWarning(ex, "UpdateRouterNode failed. NodeId: {NodeId}; EndpointUrl: {EndpointUrl}", request.NodeId, request.EndpointUrl);
            return new UpdateRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<UpdateRouterNodeControlTokenResponse> UpdateRouterNodeControlToken(
        UpdateRouterNodeControlTokenRequest request,
        ServerCallContext context)
    {
        try
        {
            await controlPlane.UpdateNodeControlTokenAsync(
                request.NodeId,
                request.ControlToken,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new UpdateRouterNodeControlTokenResponse { Updated = true };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException)
        {
            logger.LogWarning(ex, "UpdateRouterNodeControlToken failed. NodeId: {NodeId}", request.NodeId);
            return new UpdateRouterNodeControlTokenResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<DeleteRouterNodeResponse> DeleteRouterNode(
        DeleteRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            await controlPlane.DeleteNodeAsync(request.NodeId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new DeleteRouterNodeResponse { Deleted = true };
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex, "DeleteRouterNode failed. NodeId: {NodeId}", request.NodeId);
            return new DeleteRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<EnableRouterNodeResponse> EnableRouterNode(
        EnableRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            await controlPlane.EnableNodeAsync(request.NodeId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new EnableRouterNodeResponse { Enabled = true };
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex, "EnableRouterNode failed. NodeId: {NodeId}", request.NodeId);
            return new EnableRouterNodeResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<ListRouterNodesResponse> ListRouterNodes(
        ListRouterNodesRequest request,
        ServerCallContext context)
    {
        var response = new ListRouterNodesResponse();
        response.Nodes.AddRange(
            (await controlPlane.ListNodesForHealthChecksAsync(context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false))
            .Select(MapNodeSummary));
        return response;
    }

    public override async Task<GetRouterNodeDetailsResponse> GetRouterNodeDetails(
        GetRouterNodeDetailsRequest request,
        ServerCallContext context)
    {
        var cancellationToken = context?.CancellationToken ?? CancellationToken.None;
        var node = await controlPlane.GetRouterNodeAsync(request.NodeId, cancellationToken).ConfigureAwait(false);
        if (node is null)
            return new GetRouterNodeDetailsResponse { ErrorCode = ErrorCodes.ValidationFailed };

        var response = new GetRouterNodeDetailsResponse { Node = MapNodeSummary(node) };
        var apiErrors = new List<string>();

        try
        {
            using var quotaTimeoutCts = CreateProviderTimeout(cancellationToken);
            var quota = await providerClient.GetProviderQuotaAsync(
                node,
                request.IncludeInactiveProviders,
                quotaTimeoutCts.Token).ConfigureAwait(false);

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
        catch (Exception ex) when (IsProviderDetailsError(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Failed to load provider quota for router node. NodeId: {NodeId}", node.Id);
            apiErrors.Add("Provider quota unavailable: " + ex.Message);
        }

        try
        {
            using var keysTimeoutCts = CreateProviderTimeout(cancellationToken);
            var keys = await providerClient.ListIntegrationKeysAsync(
                node,
                keysTimeoutCts.Token).ConfigureAwait(false);

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
        catch (Exception ex) when (IsProviderDetailsError(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Failed to load integration keys for router node. NodeId: {NodeId}", node.Id);
            apiErrors.Add("Integration keys unavailable: " + ex.Message);
        }

        response.ApiError = string.Join(" ", apiErrors);
        return response;
    }

    private static CancellationTokenSource CreateProviderTimeout(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(NodeDetailsProviderTimeout);
        return cts;
    }

    private static bool IsProviderDetailsError(Exception ex, CancellationToken requestCancellationToken) =>
        ex is RouterNodeProviderException or InvalidOperationException or HttpRequestException ||
        ex is OperationCanceledException && !requestCancellationToken.IsCancellationRequested;

    public override async Task<PushUserIntegrationKeyResponse> PushUserIntegrationKey(
        PushUserIntegrationKeyRequest request,
        ServerCallContext context)
    {
        var all = await controlPlane.ListNodesForHealthChecksAsync(context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        IEnumerable<RouterNode> targets = string.IsNullOrWhiteSpace(request.NodeId)
            ? all.Where(n => n.Enabled)
            : all.Where(n => n.Id == request.NodeId);

        var pushedCount = 0;
        foreach (var node in targets)
        {
            try
            {
                var result = await providerClient.CreateIntegrationKeyAsync(
                    node, request.Name, request.RawKey, context?.CancellationToken ?? CancellationToken.None)
                    .ConfigureAwait(false);
                pushedCount++;

                if (!string.IsNullOrWhiteSpace(request.ApiKeyId) && !string.IsNullOrWhiteSpace(result.Id))
                    await repository.SaveIntegrationKeyMappingAsync(request.ApiKeyId, node.Id, result.Id, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось запушить integration key на ноду {NodeId}", node.Id);
            }
        }

        return new PushUserIntegrationKeyResponse { PushedCount = pushedCount };
    }

    public override async Task<UpdateUserIntegrationKeyStatusResponse> UpdateUserIntegrationKeyStatus(
        UpdateUserIntegrationKeyStatusRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKeyId))
            return new UpdateUserIntegrationKeyStatusResponse { UpdatedCount = 0 };

        var mappings = await repository.GetIntegrationKeyMappingsAsync(request.ApiKeyId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        var updatedCount = 0;

        foreach (var (nodeId, routerKeyId) in mappings)
        {
            var node = await repository.GetNodeAsync(nodeId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            if (node is null) continue;

            try
            {
                await providerClient.UpdateIntegrationKeyStatusAsync(
                    node, routerKeyId, request.IsActive, context?.CancellationToken ?? CancellationToken.None)
                    .ConfigureAwait(false);
                updatedCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось обновить статус integration key на ноде {NodeId}. RouterKeyId: {RouterKeyId}",
                    nodeId, routerKeyId);
            }
        }

        return new UpdateUserIntegrationKeyStatusResponse { UpdatedCount = updatedCount };
    }

    public override async Task<DeleteUserIntegrationKeyResponse> DeleteUserIntegrationKey(
        DeleteUserIntegrationKeyRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKeyId))
            return new DeleteUserIntegrationKeyResponse { DeletedCount = 0 };

        var mappings = await repository.GetIntegrationKeyMappingsAsync(request.ApiKeyId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        var deletedCount = 0;

        foreach (var (nodeId, routerKeyId) in mappings)
        {
            var node = await repository.GetNodeAsync(nodeId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            if (node is null) continue;

            try
            {
                await providerClient.DeleteIntegrationKeyAsync(
                    node, routerKeyId, context?.CancellationToken ?? CancellationToken.None)
                    .ConfigureAwait(false);
                deletedCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось удалить integration key на ноде {NodeId}. RouterKeyId: {RouterKeyId}",
                    nodeId, routerKeyId);
            }
        }

        // Связь зачищаем только после попытки физического удаления на нодах.
        await repository.RemoveIntegrationKeyMappingsAsync(request.ApiKeyId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new DeleteUserIntegrationKeyResponse { DeletedCount = deletedCount };
    }

    public override async Task<SyncNodeIntegrationKeysResponse> SyncNodeIntegrationKeys(
        SyncNodeIntegrationKeysRequest request,
        ServerCallContext context)
    {
        var node = await controlPlane.GetRouterNodeAsync(request.NodeId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        if (node is null)
            return new SyncNodeIntegrationKeysResponse { ErrorCode = ErrorCodes.ValidationFailed };

        var dbKeysResponse = await authClient.ListAllActiveApiKeysRawAsync(
            new Contracts.Grpc.Auth.V1.ListAllActiveApiKeysRawRequest(),
            cancellationToken: context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        var keysToSync = dbKeysResponse.Keys.Select(k => new RouterIntegrationKeySyncEntry
        {
            Name = k.Name,
            Key = k.RawKey
        }).ToList();

        try
        {
            var syncResult = await providerClient.SyncIntegrationKeysAsync(node, keysToSync, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            
            // Sync success - we don't have the new mapping IDs returned from the bulk endpoint currently
            // But we don't strictly need them unless revoking individually via UI, which we can handle
            // next time keys are requested or by re-fetching. For now, bulk sync just ensures they exist.
            
            return new SyncNodeIntegrationKeysResponse
            {
                AddedCount = syncResult.AddedCount,
                DisabledCount = 0,
                ReactivatedCount = 0
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to bulk sync keys on node {NodeId}", node.Id);
            return new SyncNodeIntegrationKeysResponse { ErrorCode = ErrorCodes.RouterUnavailable };
        }
    }

    private static Contracts.Grpc.ControlPlane.V1.RouterNodeSummary MapNodeSummary(RouterNode node) =>
        new()
        {
            NodeId = node.Id,
            Name = node.Name,
            EndpointUrl = node.EndpointUrl.ToString(),
            Status = (node.Enabled ? node.Status : RouterNodeStatus.Disabled).ToString(),
            InFlightRequests = node.InFlightRequests,
            LastCheckedAtUtc = node.LastCheckedAtUtc?.ToString("O") ?? string.Empty,
            ControlToken = node.ControlToken,
            CapacityWeight = node.CapacityWeight,
            LastHealthErrorCode = node.LastHealthErrorCode ?? string.Empty
        };
}
