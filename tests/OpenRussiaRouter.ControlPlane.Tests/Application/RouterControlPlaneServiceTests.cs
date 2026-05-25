using FluentAssertions;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterControlPlaneServiceTests
{
    [Fact]
    public async Task RegisterNode_HttpEndpoint_Succeeds()
    {
        // HTTP endpoints are allowed (e.g. internal k8s services)
        var service = CreateService();

        var node = await service.RegisterNodeAsync("node-a", new Uri("http://router.example"), "secret-a", 1, DateTimeOffset.UtcNow);

        node.Should().NotBeNull();
        node.EndpointUrl.Should().Be(new Uri("http://router.example"));
    }

    [Fact]
    public async Task RegisterNode_InvalidSchemeEndpoint_Throws()
    {
        var service = CreateService();

        await ((Func<Task>)(() =>
            service.RegisterNodeAsync("node-a", new Uri("ftp://router.example"), "secret-a", 1, DateTimeOffset.UtcNow))).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SelectRouterNode_HealthyNode_ReturnsLeaseAndIncrementsLoad()
    {
        var service = CreateService();
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        var lease = await service.SelectRouterNodeAsync("req-1", DateTimeOffset.UtcNow);

        lease.NodeId.Should().Be(node.Id);
        var summary = (await service.ListRouterNodesAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(1);
    }

    [Fact]
    public async Task UpdateNodeControlToken_ExistingNode_ChangesTokenUsedForLease()
    {
        var service = CreateService();
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        await service.UpdateNodeControlTokenAsync(node.Id, "secret-b");
        var lease = await service.SelectRouterNodeAsync("req-1", DateTimeOffset.UtcNow);

        lease.ControlToken.Should().Be("secret-b");
    }

    [Fact]
    public async Task UpdateNodeControlToken_MissingToken_Throws()
    {
        var service = CreateService();
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);

        await ((Func<Task>)(() => service.UpdateNodeControlTokenAsync(node.Id, ""))).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SelectRouterNode_UnhealthyNode_Throws()
    {
        var service = CreateService();
        await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);

        await ((Func<Task>)(() => service.SelectRouterNodeAsync("req-1", DateTimeOffset.UtcNow))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SelectRouterNode_ChoosesLeastLoadedHealthyNode()
    {
        var service = CreateService();
        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var nodeB = await service.RegisterNodeAsync("node-b", new Uri("https://router-b.example"), "secret-b", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeB.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        var first = await service.SelectRouterNodeAsync("req-1", DateTimeOffset.UtcNow);
        var second = await service.SelectRouterNodeAsync("req-2", DateTimeOffset.UtcNow);

        first.NodeId.Should().NotBe(second.NodeId);
    }

    [Fact]
    public async Task DisableNode_RemovesNodeFromSelection()
    {
        var service = CreateService();
        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var nodeB = await service.RegisterNodeAsync("node-b", new Uri("https://router-b.example"), "secret-b", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeB.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        await service.DisableNodeAsync(nodeA.Id);
        var lease = await service.SelectRouterNodeAsync("req-1", DateTimeOffset.UtcNow);

        lease.NodeId.Should().Be(nodeB.Id);
    }

    [Fact]
    public async Task ReleaseRouterNode_DecrementsLoad()
    {
        var service = CreateService();
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        var lease = await service.SelectRouterNodeAsync("req-1", DateTimeOffset.UtcNow);

        await service.ReleaseRouterNodeAsync(lease.LeaseId);

        var summary = (await service.ListRouterNodesAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(0);
    }

    [Fact]
    public async Task ReleaseExpiredLeases_DecrementsLoad()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, now);
        await service.SelectRouterNodeAsync("req-1", now);

        await service.ReleaseExpiredLeasesAsync(now.AddMinutes(6));

        var summary = (await service.ListRouterNodesAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(0);
    }

    private static RouterControlPlaneService CreateService()
    {
        return new RouterControlPlaneService(new InMemoryControlPlaneRepository());
    }
}
