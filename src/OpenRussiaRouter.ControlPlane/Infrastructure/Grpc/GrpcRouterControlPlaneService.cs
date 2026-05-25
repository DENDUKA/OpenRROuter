using System.Net;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Grpc;

public sealed class GrpcRouterControlPlaneService(
    RouterControlPlaneService controlPlane,
    IControlPlaneRepository repository,
    IRouterNodeProvider providerClient,
    ILogger<GrpcRouterControlPlaneService> logger)
    : ControlGrpc.RouterControlPlaneBase
{
    public override async Task<SelectRouterNodeResponse> SelectRouterNode(
        SelectRouterNodeRequest request,
        ServerCallContext context)
    {
        try
        {
            var lease = await controlPlane.SelectRouterNodeAsync(request.RequestId, DateTimeOffset.UtcNow, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
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
            logger.LogWarning(ex, "Failed to load provider quota for router node. NodeId: {NodeId}", node.Id);
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
            logger.LogWarning(ex, "Failed to load integration keys for router node. NodeId: {NodeId}", node.Id);
            apiErrors.Add("Integration keys unavailable: " + ex.Message);
        }

        response.ApiError = string.Join(" ", apiErrors);
        return response;
    }

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

    public override async Task<RevokeUserIntegrationKeyResponse> RevokeUserIntegrationKey(
        RevokeUserIntegrationKeyRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKeyId))
            return new RevokeUserIntegrationKeyResponse { RevokedCount = 0 };

        var mappings = await repository.GetIntegrationKeyMappingsAsync(request.ApiKeyId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        var revokedCount = 0;

        foreach (var (nodeId, routerKeyId) in mappings)
        {
            var node = await repository.GetNodeAsync(nodeId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            if (node is null) continue;

            try
            {
                await providerClient.UpdateIntegrationKeyStatusAsync(
                    node, routerKeyId, isActive: false, context?.CancellationToken ?? CancellationToken.None)
                    .ConfigureAwait(false);
                revokedCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось отозвать integration key на ноде {NodeId}. RouterKeyId: {RouterKeyId}",
                    nodeId, routerKeyId);
            }
        }

        await repository.RemoveIntegrationKeyMappingsAsync(request.ApiKeyId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new RevokeUserIntegrationKeyResponse { RevokedCount = revokedCount };
    }

    public override async Task<ListAvailableNodeModelsResponse> ListAvailableNodeModels(
        ListAvailableNodeModelsRequest request,
        ServerCallContext context)
    {
        var cancellationToken = context?.CancellationToken ?? CancellationToken.None;
        var nodes = (await controlPlane.ListNodesForHealthChecksAsync(cancellationToken).ConfigureAwait(false))
            .Where(n => n.Enabled);

        var nodeResults = await Task.WhenAll(
            nodes.Select(node => CollectNodeCatalogAsync(node, cancellationToken))
        ).ConfigureAwait(false);

        var modelIds = nodeResults
            .Where(result => !result.Unreachable)
            .SelectMany(result => result.Observations)
            .Select(observation => observation.UpstreamId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet()
            .Order()
            .ToList();

        var response = new ListAvailableNodeModelsResponse();
        response.ModelIds.AddRange(modelIds);
        return response;
    }

    public override async Task<ListNodeModelCatalogResponse> ListNodeModelCatalog(
        ListNodeModelCatalogRequest request,
        ServerCallContext context)
    {
        var cancellationToken = context?.CancellationToken ?? CancellationToken.None;
        var nodes = (await controlPlane.ListNodesForHealthChecksAsync(cancellationToken).ConfigureAwait(false))
            .Where(n => n.Enabled)
            .ToList();

        var response = new ListNodeModelCatalogResponse();
        if (nodes.Count == 0)
            return response;

        var nodeResults = await Task.WhenAll(
            nodes.Select(node => CollectNodeCatalogAsync(node, cancellationToken))
        ).ConfigureAwait(false);

        var accumulators = new Dictionary<string, CatalogAccumulator>(StringComparer.Ordinal);
        foreach (var result in nodeResults)
        {
            if (result.Unreachable)
                continue;

            foreach (var observation in result.Observations)
            {
                if (!accumulators.TryGetValue(observation.UpstreamId, out var acc))
                {
                    acc = new CatalogAccumulator { UpstreamId = observation.UpstreamId };
                    accumulators[observation.UpstreamId] = acc;
                }

                (acc.PublicId, acc.PublicIdRank) =
                    ChooseField(acc.PublicId, acc.PublicIdRank, observation.PublicId, observation.Rank);
                (acc.DisplayName, acc.DisplayNameRank) =
                    ChooseField(acc.DisplayName, acc.DisplayNameRank, observation.DisplayName, observation.Rank);
                (acc.ModelType, acc.ModelTypeRank) =
                    ChooseField(acc.ModelType, acc.ModelTypeRank, observation.ModelType, observation.Rank);

                foreach (var source in observation.SourceProviders)
                {
                    if (!string.IsNullOrWhiteSpace(source))
                        acc.SourceProviders.Add(source);
                }

                acc.NodeIds.Add(result.NodeId);
            }
        }

        foreach (var acc in accumulators.Values.OrderBy(a => a.UpstreamId, StringComparer.Ordinal))
        {
            var entry = new UpstreamModelEntry
            {
                UpstreamId = acc.UpstreamId,
                PublicId = string.IsNullOrWhiteSpace(acc.PublicId) ? acc.UpstreamId : acc.PublicId,
                DisplayName = acc.DisplayName,
                ModelType = acc.ModelType
            };
            entry.SourceProviders.AddRange(acc.SourceProviders.OrderBy(s => s, StringComparer.Ordinal));
            entry.NodeIds.AddRange(acc.NodeIds.OrderBy(s => s, StringComparer.Ordinal));
            response.Entries.Add(entry);
        }

        foreach (var result in nodeResults.Where(r => r.Unreachable).OrderBy(r => r.NodeId, StringComparer.Ordinal))
        {
            response.UnreachableNodeIds.Add(result.NodeId);
            response.UnreachableNodes.Add(new UnreachableNodeInfo
            {
                NodeId = result.NodeId,
                Request = result.Request ?? string.Empty,
                Response = result.Response ?? string.Empty
            });
        }

        return response;
    }

    private async Task<NodeCatalogResult> CollectNodeCatalogAsync(RouterNode node, CancellationToken cancellationToken)
    {
        RouterProviderListResponse providerList;
        try
        {
            providerList = await providerClient.ListProvidersAsync(node, cancellationToken).ConfigureAwait(false);
        }
        catch (RouterNodeProviderException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await CollectFallbackModelsAsync(node, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientProviderError(ex, cancellationToken))
        {
            logger.LogWarning(ex, "ListProviders failed for router node. NodeId: {NodeId}", node.Id);
            return new NodeCatalogResult(
                node.Id,
                Unreachable: true,
                [],
                FormatRequest(ex, "GET", new Uri(node.EndpointUrl, "/api/integrations/providers").ToString()),
                FormatResponse(ex));
        }

        var providerKeys = providerList.Connections
            .Where(connection => connection.IsActive && !string.IsNullOrWhiteSpace(connection.Provider))
            .Select(connection => connection.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var observations = new List<ModelObservation>();
        foreach (var providerKey in providerKeys)
        {
            try
            {
                var providerModels = await providerClient.ListProviderModelsAsync(
                    node, providerKey, source: null, includeCompatible: true, cancellationToken).ConfigureAwait(false);

                foreach (var model in providerModels.Models)
                {
                    if (string.IsNullOrWhiteSpace(model.Id) || !model.Enabled)
                        continue;

                    var publicId = string.IsNullOrWhiteSpace(model.PublicId) ? string.Empty : model.PublicId;
                    observations.Add(new ModelObservation
                    {
                        UpstreamId = model.Id,
                        PublicId = publicId,
                        DisplayName = model.Name ?? string.Empty,
                        ModelType = model.Type ?? string.Empty,
                        SourceProviders = model.Sources ?? [],
                        Rank = 2
                    });
                }
            }
            catch (RouterNodeProviderException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }
            catch (Exception ex) when (IsTransientProviderError(ex, cancellationToken))
            {
                logger.LogWarning(ex, "ListProviderModels failed. NodeId: {NodeId}; Provider: {Provider}", node.Id, providerKey);
                continue;
            }
        }

        return new NodeCatalogResult(node.Id, Unreachable: false, observations, null, null);
    }

    private async Task<NodeCatalogResult> CollectFallbackModelsAsync(RouterNode node, CancellationToken cancellationToken)
    {
        try
        {
            var models = await providerClient.ListModelsAsync(node, cancellationToken).ConfigureAwait(false);
            var observations = models.Data
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => new ModelObservation
                {
                    UpstreamId = m.Id,
                    PublicId = m.Id,
                    DisplayName = string.Empty,
                    ModelType = string.Empty,
                    SourceProviders = [],
                    Rank = 1
                })
                .ToList();
            return new NodeCatalogResult(node.Id, Unreachable: false, observations, null, null);
        }
        catch (Exception ex) when (IsTransientProviderError(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Fallback /v1/models failed for router node. NodeId: {NodeId}", node.Id);
            return new NodeCatalogResult(
                node.Id,
                Unreachable: true,
                [],
                FormatRequest(ex, "GET", new Uri(node.EndpointUrl, "/v1/models").ToString()),
                FormatResponse(ex));
        }
    }

    private static bool IsTransientProviderError(Exception ex, CancellationToken cancellationToken) =>
        ex is RouterNodeProviderException or InvalidOperationException or HttpRequestException ||
        (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static string FormatRequest(Exception ex, string fallbackMethod, string fallbackUri)
    {
        if (ex is RouterNodeProviderException provider &&
            !string.IsNullOrWhiteSpace(provider.RequestMethod) &&
            !string.IsNullOrWhiteSpace(provider.RequestUri))
        {
            return $"{provider.RequestMethod} {provider.RequestUri}";
        }
        return $"{fallbackMethod} {fallbackUri}";
    }

    private static string FormatResponse(Exception ex)
    {
        if (ex is RouterNodeProviderException provider)
        {
            var body = provider.ResponseBody;
            const int maxBody = 1024;
            if (!string.IsNullOrEmpty(body) && body.Length > maxBody)
            {
                body = body[..maxBody] + "… (truncated)";
            }
            var statusLine = $"HTTP {(int)provider.StatusCode} {provider.StatusCode}";
            return string.IsNullOrWhiteSpace(body) ? statusLine : $"{statusLine}\n{body}";
        }

        if (ex is TaskCanceledException tce)
            return tce.InnerException is TimeoutException
                ? "TimeoutException: request timed out"
                : $"TaskCanceledException: {tce.Message}";

        if (ex is HttpRequestException hre)
            return $"HttpRequestException: {hre.Message}";

        if (ex is InvalidOperationException ioe)
            return $"InvalidOperationException: {ioe.Message}";

        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private static (string Value, int Rank) ChooseField(string current, int currentRank, string incoming, int incomingRank)
    {
        if (string.IsNullOrWhiteSpace(incoming))
            return (current, currentRank);

        if (incomingRank > currentRank ||
            (incomingRank == currentRank && string.IsNullOrWhiteSpace(current)))
        {
            return (incoming, incomingRank);
        }

        return (current, currentRank);
    }

    private sealed record NodeCatalogResult(
        string NodeId,
        bool Unreachable,
        IReadOnlyList<ModelObservation> Observations,
        string? Request,
        string? Response);

    private sealed class ModelObservation
    {
        public string UpstreamId { get; init; } = string.Empty;
        public string PublicId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string ModelType { get; init; } = string.Empty;
        public IReadOnlyList<string> SourceProviders { get; init; } = [];
        public int Rank { get; init; }
    }

    private sealed class CatalogAccumulator
    {
        public string UpstreamId { get; init; } = string.Empty;
        public string PublicId { get; set; } = string.Empty;
        public int PublicIdRank { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int DisplayNameRank { get; set; }
        public string ModelType { get; set; } = string.Empty;
        public int ModelTypeRank { get; set; }
        public HashSet<string> SourceProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
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
