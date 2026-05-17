using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.WebApp.Services.ControlPlane;

public sealed class ControlPlaneClient(ControlGrpc.RouterControlPlaneClient client)
{
    public async Task<IReadOnlyList<RouterNodeInfo>> ListRouterNodesAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListRouterNodesAsync(new OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.ListRouterNodesRequest(), cancellationToken: cancellationToken);
        return response.Nodes.Select(n => new RouterNodeInfo(
            n.NodeId,
            n.Name,
            n.EndpointUrl,
            n.Status.ToLowerInvariant(),
            n.InFlightRequests,
            string.IsNullOrWhiteSpace(n.LastCheckedAtUtc)
                ? null
                : DateTimeOffset.Parse(n.LastCheckedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
    }

    public async Task DisableRouterNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await client.DisableRouterNodeAsync(new OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.DisableRouterNodeRequest { NodeId = nodeId }, cancellationToken: cancellationToken);
    }

    public async Task EnableRouterNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await client.EnableRouterNodeAsync(new OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.EnableRouterNodeRequest { NodeId = nodeId }, cancellationToken: cancellationToken);
    }

    public async Task DeleteRouterNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await client.DeleteRouterNodeAsync(new OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.DeleteRouterNodeRequest { NodeId = nodeId }, cancellationToken: cancellationToken);
    }

    public async Task RegisterRouterNodeAsync(string name, string endpointUrl, string? tokenSecretName, int capacityWeight, CancellationToken cancellationToken = default)
    {
        var response = await client.RegisterRouterNodeAsync(new OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RegisterRouterNodeRequest
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
}