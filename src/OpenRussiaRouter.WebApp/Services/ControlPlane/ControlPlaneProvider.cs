using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.WebApp.Services.ControlPlane;

public sealed class ControlPlaneProvider(
    ControlGrpc.RouterControlPlaneClient client)
{
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

    public async Task DeleteUserIntegrationKeyAsync(string apiKeyId, CancellationToken cancellationToken = default)
    {
        await client.DeleteUserIntegrationKeyAsync(new DeleteUserIntegrationKeyRequest
        {
            ApiKeyId = apiKeyId
        }, cancellationToken: cancellationToken);
    }

    public async Task<(int Added, int Disabled, int Reactivated)> SyncNodeIntegrationKeysAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var response = await client.SyncNodeIntegrationKeysAsync(new SyncNodeIntegrationKeysRequest
        {
            NodeId = nodeId ?? string.Empty
        }, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new InvalidOperationException($"SyncNodeIntegrationKeys failed: {response.ErrorCode}");
        }

        return (response.AddedCount, response.DisabledCount, response.ReactivatedCount);
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
