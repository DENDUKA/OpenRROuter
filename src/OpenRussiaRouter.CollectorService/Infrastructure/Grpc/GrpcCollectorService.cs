using Grpc.Core;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.CollectorService.Infrastructure.Grpc;

public sealed class GrpcCollectorService(CollectorSnapshotStore store) : Collector.CollectorBase
{
    public override Task<GetCapabilitiesSnapshotResponse> GetCapabilitiesSnapshot(
        GetCapabilitiesSnapshotRequest request, ServerCallContext context)
    {
        var snapshot = store.Current;
        var response = new GetCapabilitiesSnapshotResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty
        };
        foreach (var (nodeId, modelIds) in CatalogProjection.Capabilities(snapshot))
        {
            var cap = new NodeCapability { NodeId = nodeId };
            cap.ModelIds.AddRange(modelIds);
            response.Nodes.Add(cap);
        }
        return Task.FromResult(response);
    }

    public override Task<ListNodeModelCatalogResponse> ListNodeModelCatalog(
        ListNodeModelCatalogRequest request, ServerCallContext context)
    {
        var snapshot = store.Current;
        var response = new ListNodeModelCatalogResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty
        };

        foreach (var entry in CatalogProjection.Catalog(snapshot))
        {
            var e = new UpstreamModelEntry
            {
                UpstreamId = entry.UpstreamId,
                PublicId = entry.PublicId,
                DisplayName = entry.DisplayName,
                ModelType = entry.ModelType
            };
            e.SourceProviders.AddRange(entry.SourceProviders);
            e.NodeIds.AddRange(entry.NodeIds);
            response.Entries.Add(e);
        }

        foreach (var node in snapshot.Nodes.Where(n => n.Unreachable).OrderBy(n => n.NodeId, StringComparer.Ordinal))
        {
            response.UnreachableNodeIds.Add(node.NodeId);
            response.UnreachableNodes.Add(new UnreachableNodeInfo
            {
                NodeId = node.NodeId,
                Request = node.Request ?? string.Empty,
                Response = node.Response ?? string.Empty
            });
        }

        return Task.FromResult(response);
    }

    public override Task<GetNodeModelsResponse> GetNodeModels(
        GetNodeModelsRequest request, ServerCallContext context)
    {
        var snapshot = store.Current;
        var models = CatalogProjection.NodeModels(snapshot, request.NodeId);
        if (models is null)
            return Task.FromResult(new GetNodeModelsResponse { Error = "Node not found or unreachable" });

        var response = new GetNodeModelsResponse();
        response.PublicIds.AddRange(models);
        return Task.FromResult(response);
    }
}
