using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.WebApp.Services.ControlPlane;

public sealed class ControlPlaneClient(ControlGrpc.RouterControlPlaneClient client)
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

    public async Task EnableRouterNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await client.EnableRouterNodeAsync(new EnableRouterNodeRequest { NodeId = nodeId }, cancellationToken: cancellationToken);
    }

    public async Task DeleteRouterNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await client.DeleteRouterNodeAsync(new DeleteRouterNodeRequest { NodeId = nodeId }, cancellationToken: cancellationToken);
    }

    public async Task RegisterRouterNodeAsync(string name, string endpointUrl, string? tokenSecretName, int capacityWeight, CancellationToken cancellationToken = default)
    {
        var response = await client.RegisterRouterNodeAsync(new RegisterRouterNodeRequest
        {
            Name = name ?? string.Empty,
            EndpointUrl = endpointUrl ?? string.Empty,
            TokenSecretName = tokenSecretName ?? string.Empty,
            CapacityWeight = capacityWeight
        }, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new InvalidOperationException($"RegisterRouterNode failed: {response.ErrorCode}");
        }
    }

    public async Task PushUserIntegrationKeyAsync(
        string name, string rawKey, string machineId,
        CancellationToken cancellationToken = default)
    {
        await client.PushUserIntegrationKeyAsync(new PushUserIntegrationKeyRequest
        {
            Name = name,
            RawKey = rawKey,
            MachineId = machineId
        }, cancellationToken: cancellationToken);
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
            n.TokenSecretName,
            n.CapacityWeight,
            n.LastHealthErrorCode);
    }
}
