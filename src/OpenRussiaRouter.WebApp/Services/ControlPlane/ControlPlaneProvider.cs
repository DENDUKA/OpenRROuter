using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.WebApp.Services.ControlPlane;

public sealed class ControlPlaneProvider(
    ControlGrpc.RouterControlPlaneClient client,
    ILogger<ControlPlaneProvider> logger)
{
    private static readonly TimeSpan NodeModelDropdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NodeModelCatalogTimeout = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<RouterNodeInfo>> ListRouterNodesAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListRouterNodesAsync(new ListRouterNodesRequest(), cancellationToken: cancellationToken);
        return response.Nodes.Select(MapNode).ToList();
    }

    public async Task<RouterNodeDetails> GetRouterNodeDetailsAsync(
        string nodeId,
        bool includeInactiveProviders = true,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetRouterNodeDetailsAsync(new GetRouterNodeDetailsRequest
        {
            NodeId = nodeId ?? string.Empty,
            IncludeInactiveProviders = includeInactiveProviders
        }, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new InvalidOperationException($"GetRouterNodeDetails failed: {response.ErrorCode}");
        }

        return new RouterNodeDetails(
            MapNode(response.Node),
            response.Providers.Select(p => new RouterProviderQuotaInfo(
                p.ConnectionId,
                p.Provider,
                p.Name,
                p.AuthType,
                p.IsActive,
                string.IsNullOrWhiteSpace(p.TestStatus) ? null : p.TestStatus,
                p.QuotaSupported,
                string.IsNullOrWhiteSpace(p.UsageJson) ? null : p.UsageJson,
                string.IsNullOrWhiteSpace(p.Error) ? null : p.Error)).ToList(),
            response.Keys.Select(k => new RouterIntegrationKeyInfo(
                k.Id,
                k.Name,
                k.KeyPreview,
                k.MachineId,
                k.IsActive,
                k.ManagedBy,
                k.CreatedAt)).ToList(),
            string.IsNullOrWhiteSpace(response.ApiError) ? null : response.ApiError);
    }

    public async Task DisableRouterNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await client.DisableRouterNodeAsync(new DisableRouterNodeRequest { NodeId = nodeId }, cancellationToken: cancellationToken);
    }

    public async Task UpdateRouterNodeControlTokenAsync(
        string nodeId,
        string controlToken,
        CancellationToken cancellationToken = default)
    {
        var response = await client.UpdateRouterNodeControlTokenAsync(new UpdateRouterNodeControlTokenRequest
        {
            NodeId = nodeId ?? string.Empty,
            ControlToken = controlToken ?? string.Empty
        }, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new InvalidOperationException($"UpdateRouterNodeControlToken failed: {response.ErrorCode}");
        }
    }

    public async Task UpdateRouterNodeAsync(
        string nodeId,
        string name,
        string endpointUrl,
        string controlToken,
        int capacityWeight,
        CancellationToken cancellationToken = default)
    {
        var response = await client.UpdateRouterNodeAsync(new UpdateRouterNodeRequest
        {
            NodeId = nodeId ?? string.Empty,
            Name = name ?? string.Empty,
            EndpointUrl = endpointUrl ?? string.Empty,
            ControlToken = controlToken ?? string.Empty,
            CapacityWeight = capacityWeight
        }, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new InvalidOperationException($"UpdateRouterNode failed: {response.ErrorCode}");
        }
    }

    public async Task EnableRouterNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await client.EnableRouterNodeAsync(new EnableRouterNodeRequest { NodeId = nodeId }, cancellationToken: cancellationToken);
    }

    public async Task DeleteRouterNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await client.DeleteRouterNodeAsync(new DeleteRouterNodeRequest { NodeId = nodeId }, cancellationToken: cancellationToken);
    }

    public async Task RegisterRouterNodeAsync(string name, string endpointUrl, string? controlToken, int capacityWeight, CancellationToken cancellationToken = default)
    {
        var response = await client.RegisterRouterNodeAsync(new RegisterRouterNodeRequest
        {
            Name = name ?? string.Empty,
            EndpointUrl = endpointUrl ?? string.Empty,
            ControlToken = controlToken ?? string.Empty,
            CapacityWeight = capacityWeight
        }, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new InvalidOperationException($"RegisterRouterNode failed: {response.ErrorCode}");
        }
    }

    public async Task PushUserIntegrationKeyAsync(
        string name, string rawKey, string machineId, string apiKeyId,
        string? nodeId = null,
        CancellationToken cancellationToken = default)
    {
        await client.PushUserIntegrationKeyAsync(new PushUserIntegrationKeyRequest
        {
            Name = name,
            RawKey = rawKey,
            MachineId = machineId,
            ApiKeyId = apiKeyId,
            NodeId = nodeId ?? string.Empty
        }, cancellationToken: cancellationToken);
    }

    public async Task RevokeUserIntegrationKeyAsync(string apiKeyId, CancellationToken cancellationToken = default)
    {
        await client.RevokeUserIntegrationKeyAsync(new RevokeUserIntegrationKeyRequest
        {
            ApiKeyId = apiKeyId
        }, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListAvailableNodeModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(NodeModelDropdownTimeout);

            var response = await client.ListAvailableNodeModelsAsync(
                new ListAvailableNodeModelsRequest(),
                cancellationToken: timeoutCts.Token);
            return response.ModelIds.ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<string>> GetNodeModelsAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(NodeModelDropdownTimeout);

            var response = await client.GetNodeModelsAsync(
                new GetNodeModelsRequest { NodeId = nodeId ?? string.Empty },
                cancellationToken: timeoutCts.Token);

            return string.IsNullOrWhiteSpace(response.Error)
                ? response.PublicIds.ToList()
                : [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get models for node {NodeId}", nodeId);
            return [];
        }
    }

    public async Task<NodeModelCatalogResult> ListNodeModelCatalogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(NodeModelCatalogTimeout);

            var response = await client.ListNodeModelCatalogAsync(
                new ListNodeModelCatalogRequest(),
                cancellationToken: timeoutCts.Token);

            return new NodeModelCatalogResult(
                Entries: response.Entries.Select(e => new NodeModelCatalogEntry(
                    UpstreamId: e.UpstreamId,
                    PublicId: e.PublicId,
                    DisplayName: e.DisplayName,
                    ModelType: e.ModelType,
                    SourceProviders: e.SourceProviders.ToList(),
                    NodeIds: e.NodeIds.ToList())).ToList(),
                UnreachableNodeIds: response.UnreachableNodeIds.ToList(),
                UnreachableNodes: response.UnreachableNodes.Select(u => new UnreachableNodeDetail(
                    NodeId: u.NodeId,
                    Request: u.Request,
                    Response: u.Response)).ToList(),
                Error: null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load node model catalog");
            return new NodeModelCatalogResult(
                Entries: [],
                UnreachableNodeIds: [],
                UnreachableNodes: [],
                Error: $"Failed to load node model catalog: {ex.Message}");
        }
    }

    private static RouterNodeInfo MapNode(RouterNodeSummary n)
    {
        return new RouterNodeInfo(
            n.NodeId,
            n.Name,
            n.EndpointUrl,
            n.Status.ToLowerInvariant(),
            n.InFlightRequests,
            string.IsNullOrWhiteSpace(n.LastCheckedAtUtc)
                ? null
                : DateTimeOffset.Parse(n.LastCheckedAtUtc, System.Globalization.CultureInfo.InvariantCulture),
            n.ControlToken,
            n.CapacityWeight,
            n.LastHealthErrorCode);
    }
}
