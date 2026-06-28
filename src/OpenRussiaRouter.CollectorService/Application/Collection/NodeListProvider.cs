using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;

namespace OpenRussiaRouter.CollectorService.Application.Collection;

public interface INodeListProvider
{
    Task<IReadOnlyList<CollectorNode>> GetNodesAsync(CancellationToken cancellationToken);
}

public sealed class NodeListProvider(RouterControlPlane.RouterControlPlaneClient client) : INodeListProvider
{
    public async Task<IReadOnlyList<CollectorNode>> GetNodesAsync(CancellationToken cancellationToken)
    {
        var response = await client.ListRouterNodesForInternalConsumersAsync(
            new ListRouterNodesRequest(), cancellationToken: cancellationToken);

        var nodes = new List<CollectorNode>(response.Nodes.Count);
        foreach (var n in response.Nodes)
        {
            if (!Uri.TryCreate(n.EndpointUrl, UriKind.Absolute, out var endpoint))
                continue;
            nodes.Add(new CollectorNode(
                NodeId: n.NodeId,
                EndpointUrl: endpoint,
                ControlToken: n.ControlToken,
                // ControlPlane maps a disabled node's status to "Disabled"; treat anything not
                // "Disabled" as enabled for polling purposes.
                Enabled: !string.Equals(n.Status, "Disabled", StringComparison.OrdinalIgnoreCase),
                Status: n.Status,
                HasBeenHealthChecked: !string.IsNullOrWhiteSpace(n.LastCheckedAtUtc)));
        }
        return nodes;
    }
}
