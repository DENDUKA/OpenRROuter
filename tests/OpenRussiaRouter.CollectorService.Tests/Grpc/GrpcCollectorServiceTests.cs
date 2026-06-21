using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.OpenRouter;
using OpenRussiaRouter.CollectorService.Infrastructure.Grpc;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.CollectorService.Tests.Grpc;

public class GrpcCollectorServiceTests
{
    private static GrpcCollectorService Build(CollectorSnapshot snapshot)
    {
        var store = new CollectorSnapshotStore();
        store.Set(snapshot);
        return new GrpcCollectorService(store, new OpenRouterCatalogStore());
    }

    [Fact]
    public async Task GetCapabilitiesSnapshot_ProjectsNodesAndTimestamp()
    {
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("n1", false,
                [new ModelObservation { UpstreamId = "u", PublicId = "p", Rank = 2 }], null, null)],
            DateTimeOffset.UnixEpoch);
        var service = Build(snapshot);

        var response = await service.GetCapabilitiesSnapshot(new GetCapabilitiesSnapshotRequest(), context: null!);

        response.Nodes.Should().ContainSingle();
        response.Nodes[0].NodeId.Should().Be("n1");
        response.Nodes[0].ModelIds.Should().Equal("p");
        response.CollectedAtUtc.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ListNodeModelCatalog_IncludesUnreachableNodes()
    {
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("dead", true, [], "GET http://dead/x", "HTTP 500")],
            DateTimeOffset.UnixEpoch);
        var service = Build(snapshot);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        response.Entries.Should().BeEmpty();
        response.UnreachableNodeIds.Should().Equal("dead");
        response.UnreachableNodes.Should().ContainSingle(u => u.NodeId == "dead" && u.Response == "HTTP 500");
    }

    [Fact]
    public async Task GetNodeModels_UnknownNode_ReturnsError()
    {
        var service = Build(CollectorSnapshot.Empty);

        var response = await service.GetNodeModels(new GetNodeModelsRequest { NodeId = "nope" }, context: null!);

        response.Error.Should().NotBeEmpty();
        response.PublicIds.Should().BeEmpty();
    }
}
