using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Application.Contracts;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain;
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

    private static CancellationToken Ct(ServerCallContext? context) =>
        context?.CancellationToken ?? CancellationToken.None;

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
                Ct(context)).ConfigureAwait(false);
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
        await controlPlane.ReleaseRouterNodeAsync(request.LeaseId, Ct(context)).ConfigureAwait(false);
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
                Ct(context)).ConfigureAwait(false);

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
            await controlPlane.DisableNodeAsync(request.NodeId, Ct(context)).ConfigureAwait(false);
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
                Ct(context)).ConfigureAwait(false);
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
                Ct(context)).ConfigureAwait(false);
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
            await controlPlane.DeleteNodeAsync(request.NodeId, Ct(context)).ConfigureAwait(false);
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
            await controlPlane.EnableNodeAsync(request.NodeId, Ct(context)).ConfigureAwait(false);
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
            (await controlPlane.ListNodesForHealthChecksAsync(Ct(context)).ConfigureAwait(false))
            .Select(MapNodeSummary));
        return response;
    }

    public override async Task<ListRouterNodesForInternalConsumersResponse> ListRouterNodesForInternalConsumers(
        ListRouterNodesRequest request,
        ServerCallContext context)
    {
        var response = new ListRouterNodesForInternalConsumersResponse();
        response.Nodes.AddRange(
            (await controlPlane.ListNodesForHealthChecksAsync(Ct(context)).ConfigureAwait(false))
            .Select(MapNodeAdminSummary));
        return response;
    }

    public override async Task<GetRouterNodeDetailsResponse> GetRouterNodeDetails(
        GetRouterNodeDetailsRequest request,
        ServerCallContext context)
    {
        var cancellationToken = Ct(context);
        var node = await controlPlane.GetRouterNodeAsync(request.NodeId, cancellationToken).ConfigureAwait(false);
        if (node is null)
            return new GetRouterNodeDetailsResponse { ErrorCode = ErrorCodes.ValidationFailed };

        var response = new GetRouterNodeDetailsResponse { Node = MapNodeAdminSummary(node) };
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
        var all = await controlPlane.ListNodesForHealthChecksAsync(Ct(context)).ConfigureAwait(false);

        IEnumerable<RouterNode> targets = string.IsNullOrWhiteSpace(request.NodeId)
            ? all.Where(n => n.Enabled)
            : all.Where(n => n.Id == request.NodeId);

        var pushedCount = 0;
        foreach (var node in targets)
        {
            try
            {
                var result = await providerClient.CreateIntegrationKeyAsync(
                    node, request.Name, request.RawKey, Ct(context))
                    .ConfigureAwait(false);
                pushedCount++;

                if (!string.IsNullOrWhiteSpace(request.ApiKeyId) && !string.IsNullOrWhiteSpace(result.Id))
                    await repository.SaveIntegrationKeyMappingAsync(request.ApiKeyId, node.Id, result.Id, Ct(context)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to push integration key to node {NodeId}", node.Id);
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

        var mappings = await repository.GetIntegrationKeyMappingsAsync(request.ApiKeyId, Ct(context)).ConfigureAwait(false);
        var updatedCount = 0;

        foreach (var (nodeId, routerKeyId) in mappings)
        {
            var node = await repository.GetNodeAsync(nodeId, Ct(context)).ConfigureAwait(false);
            if (node is null) continue;

            try
            {
                await providerClient.UpdateIntegrationKeyStatusAsync(
                    node, routerKeyId, request.IsActive, Ct(context))
                    .ConfigureAwait(false);
                updatedCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update integration key status on node {NodeId}. RouterKeyId: {RouterKeyId}",
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

        var mappings = await repository.GetIntegrationKeyMappingsAsync(request.ApiKeyId, Ct(context)).ConfigureAwait(false);
        var deletedCount = 0;

        foreach (var (nodeId, routerKeyId) in mappings)
        {
            var node = await repository.GetNodeAsync(nodeId, Ct(context)).ConfigureAwait(false);
            if (node is null) continue;

            try
            {
                await providerClient.DeleteIntegrationKeyAsync(
                    node, routerKeyId, Ct(context))
                    .ConfigureAwait(false);
                deletedCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete integration key on node {NodeId}. RouterKeyId: {RouterKeyId}",
                    nodeId, routerKeyId);
            }
        }

        // Clear mappings only after attempting physical deletion on the nodes.
        await repository.RemoveIntegrationKeyMappingsAsync(request.ApiKeyId, Ct(context)).ConfigureAwait(false);

        return new DeleteUserIntegrationKeyResponse { DeletedCount = deletedCount };
    }

    public override async Task<SyncNodeIntegrationKeysResponse> SyncNodeIntegrationKeys(
        SyncNodeIntegrationKeysRequest request,
        ServerCallContext context)
    {
        var node = await controlPlane.GetRouterNodeAsync(request.NodeId, Ct(context)).ConfigureAwait(false);
        if (node is null)
            return new SyncNodeIntegrationKeysResponse { ErrorCode = ErrorCodes.ValidationFailed };

        var dbKeysResponse = await authClient.ListAllNonDeletedApiKeysRawAsync(
            new Contracts.Grpc.Auth.V1.ListAllNonDeletedApiKeysRawRequest(),
            cancellationToken: Ct(context)).ConfigureAwait(false);

        var keysToSync = dbKeysResponse.Keys.Select(k => new RouterIntegrationKeySyncEntry
        {
            Name = k.Name,
            Key = k.RawKey,
            IsActive = k.IsActive
        }).ToList();

        try
        {
            var syncResult = await providerClient.SyncIntegrationKeysAsync(node, keysToSync, Ct(context)).ConfigureAwait(false);

            return new SyncNodeIntegrationKeysResponse
            {
                AddedCount = syncResult.AddedCount,
                DisabledCount = syncResult.DisabledCount,
                ReactivatedCount = syncResult.ReactivatedCount,
                DeletedCount = syncResult.DeletedCount
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to bulk sync keys on node {NodeId}", node.Id);
            return new SyncNodeIntegrationKeysResponse { ErrorCode = ErrorCodes.RouterUnavailable };
        }
    }

    public override async Task<CheckNodeIntegrationKeysSyncResponse> CheckNodeIntegrationKeysSync(
        CheckNodeIntegrationKeysSyncRequest request,
        ServerCallContext context)
    {
        var node = await controlPlane.GetRouterNodeAsync(request.NodeId, Ct(context)).ConfigureAwait(false);
        if (node is null)
            return new CheckNodeIntegrationKeysSyncResponse { ErrorCode = ErrorCodes.ValidationFailed };

        var dbKeysResponse = await authClient.ListAllNonDeletedApiKeysRawAsync(
            new Contracts.Grpc.Auth.V1.ListAllNonDeletedApiKeysRawRequest(),
            cancellationToken: Ct(context)).ConfigureAwait(false);

        try
        {
            var nodeKeysResponse = await providerClient.ListIntegrationKeysAsync(
                node,
                Ct(context)).ConfigureAwait(false);

            var dbKeysDict = dbKeysResponse.Keys.ToDictionary(k => k.RawKey, k => k);
            var nodeKeysDict = nodeKeysResponse.Keys.ToDictionary(k => k.Id, k => k);

            var toAdd = 0;
            var toDisable = 0;
            var toReactivate = 0;
            var toDelete = 0;

            foreach (var dbKey in dbKeysResponse.Keys)
            {
                if (nodeKeysDict.TryGetValue(dbKey.RawKey, out var nodeKey))
                {
                    if (dbKey.IsActive && !nodeKey.IsActive)
                        toReactivate++;
                    else if (!dbKey.IsActive && nodeKey.IsActive)
                        toDisable++;
                }
                else
                {
                    toAdd++;
                }
            }

            foreach (var nodeKey in nodeKeysResponse.Keys)
            {
                if (nodeKey.ManagedBy == "service" && !dbKeysDict.ContainsKey(nodeKey.Id))
                {
                    toDelete++;
                }
            }

            return new CheckNodeIntegrationKeysSyncResponse
            {
                ToAddCount = toAdd,
                ToDisableCount = toDisable,
                ToReactivateCount = toReactivate,
                ToDeleteCount = toDelete
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check bulk sync keys on node {NodeId}", node.Id);
            return new CheckNodeIntegrationKeysSyncResponse { ErrorCode = ErrorCodes.RouterUnavailable };
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
            CapacityWeight = node.CapacityWeight,
            LastHealthErrorCode = node.LastHealthErrorCode ?? string.Empty
        };

    private static Contracts.Grpc.ControlPlane.V1.RouterNodeAdminSummary MapNodeAdminSummary(RouterNode node) =>
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
