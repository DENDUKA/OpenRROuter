using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class GrpcRouterControlPlaneServiceTests
{
    private static readonly IRouterKeySyncService NoOpKeySync = new NoOpRouterKeySyncService();

    [Fact]
    public async Task SelectRouterNode_HealthyNode_ReturnsLease()
    {
        var control = CreateControlPlaneWithHealthyNode();
        var service = new GrpcRouterControlPlaneService(control, NoOpKeySync);

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
        var service = new GrpcRouterControlPlaneService(new RouterControlPlaneService(new InMemoryControlPlaneRepository()), NoOpKeySync);

        var response = await service.SelectRouterNode(
            new SelectRouterNodeRequest { RequestId = "req-1", ModelId = "gpt-test" },
            context: null!);

        Assert.Equal(ErrorCodes.RouterUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task ReleaseRouterNode_ExistingLease_ReturnsAcceptedAndReleasesLoad()
    {
        var control = CreateControlPlaneWithHealthyNode();
        var service = new GrpcRouterControlPlaneService(control, NoOpKeySync);
        var lease = control.SelectRouterNode("req-1", DateTimeOffset.UtcNow);

        var response = await service.ReleaseRouterNode(
            new ReleaseRouterNodeRequest { LeaseId = lease.LeaseId, Status = "succeeded" },
            context: null!);

        Assert.True(response.Accepted);
        var summary = Assert.Single(control.ListRouterNodes());
        Assert.Equal(0, summary.InFlightRequests);
    }

    [Fact]
    public async Task RegisterAndListRouterNode_ValidRequest_ReturnsNodeSummary()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var service = new GrpcRouterControlPlaneService(control, NoOpKeySync);

        var registered = await service.RegisterRouterNode(new RegisterRouterNodeRequest
        {
            Name = "node-a",
            EndpointUrl = "https://router-a.example",
            TokenSecretName = "secret-a",
            CapacityWeight = 2
        }, context: null!);
        var nodes = await service.ListRouterNodes(new ListRouterNodesRequest(), context: null!);

        Assert.True(registered.Registered);
        Assert.NotEmpty(registered.NodeId);
        var node = Assert.Single(nodes.Nodes);
        Assert.Equal(registered.NodeId, node.NodeId);
        Assert.Equal("node-a", node.Name);
        Assert.Equal("https://router-a.example/", node.EndpointUrl);
    }

    [Fact]
    public async Task DisableRouterNode_ExistingNode_ReturnsDisabledAndListShowsDisabled()
    {
        var control = CreateControlPlaneWithHealthyNode();
        var service = new GrpcRouterControlPlaneService(control, NoOpKeySync);
        var nodeId = Assert.Single(control.ListRouterNodes()).Id;

        var disabled = await service.DisableRouterNode(new DisableRouterNodeRequest { NodeId = nodeId }, context: null!);
        var nodes = await service.ListRouterNodes(new ListRouterNodesRequest(), context: null!);

        Assert.True(disabled.Disabled);
        Assert.Equal("Disabled", Assert.Single(nodes.Nodes).Status);
    }

    [Fact]
    public async Task RegisterRouterNode_InvalidEndpoint_ReturnsValidationFailed()
    {
        var service = new GrpcRouterControlPlaneService(new RouterControlPlaneService(new InMemoryControlPlaneRepository()), NoOpKeySync);

        var response = await service.RegisterRouterNode(new RegisterRouterNodeRequest
        {
            Name = "node-a",
            EndpointUrl = "http://router-a.example",
            TokenSecretName = "secret-a",
            CapacityWeight = 1
        }, context: null!);

        Assert.False(response.Registered);
        Assert.Equal(ErrorCodes.ValidationFailed, response.ErrorCode);
    }

    private static RouterControlPlaneService CreateControlPlaneWithHealthyNode()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var node = control.RegisterNode(
            "node-a",
            new Uri("https://router-a.example"),
            "secret-a",
            1,
            DateTimeOffset.UtcNow);
        control.ApplyHealth(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        return control;
    }

    private sealed class NoOpRouterKeySyncService : IRouterKeySyncService
    {
        public Task SyncAllAsync(CancellationToken ct) => Task.CompletedTask;
        public Task SyncToNodeAsync(string nodeId, CancellationToken ct) => Task.CompletedTask;
        public Task RevokeKeyAsync(string prefix, CancellationToken ct) => Task.CompletedTask;
    }
}

