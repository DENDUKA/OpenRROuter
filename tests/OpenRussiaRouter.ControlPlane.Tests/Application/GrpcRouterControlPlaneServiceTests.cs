using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using OpenRussiaRouter.ControlPlane.Application;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class GrpcRouterControlPlaneServiceTests
{
    [Fact]
    public async Task SelectRouterNode_HealthyNode_ReturnsLease()
    {
        var control = CreateControlPlaneWithHealthyNode();
        var service = new GrpcRouterControlPlaneService(control);

        var response = await service.SelectRouterNode(
            new SelectRouterNodeRequest { RequestId = "req-1", ModelId = "gpt-test" },
            context: null!);

        Assert.NotEqual(string.Empty, response.LeaseId);
        Assert.NotEqual(string.Empty, response.NodeId);
        Assert.Equal("https://router-a.example/", response.EndpointUrl);
        Assert.Equal("secret-a", response.TokenSecretName);
        Assert.Equal(string.Empty, response.ErrorCode);
    }

    [Fact]
    public async Task SelectRouterNode_NoHealthyNodes_ReturnsRouterUnavailable()
    {
        var service = new GrpcRouterControlPlaneService(new RouterControlPlaneService(new InMemoryControlPlaneStore()));

        var response = await service.SelectRouterNode(
            new SelectRouterNodeRequest { RequestId = "req-1", ModelId = "gpt-test" },
            context: null!);

        Assert.Equal(ErrorCodes.RouterUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task ReleaseRouterNode_ExistingLease_ReturnsAcceptedAndReleasesLoad()
    {
        var control = CreateControlPlaneWithHealthyNode();
        var service = new GrpcRouterControlPlaneService(control);
        var lease = control.SelectRouterNode("req-1", DateTimeOffset.UtcNow);

        var response = await service.ReleaseRouterNode(
            new ReleaseRouterNodeRequest { LeaseId = lease.LeaseId, Status = "succeeded" },
            context: null!);

        Assert.True(response.Accepted);
        var summary = Assert.Single(control.ListRouterNodes());
        Assert.Equal(0, summary.InFlightRequests);
    }

    private static RouterControlPlaneService CreateControlPlaneWithHealthyNode()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneStore());
        var node = control.RegisterNode(
            "node-a",
            new Uri("https://router-a.example"),
            "secret-a",
            1,
            DateTimeOffset.UtcNow);
        control.ApplyHealth(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        return control;
    }
}

