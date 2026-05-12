using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterControlPlaneServiceTests
{
    [Fact]
    public void RegisterNode_HttpEndpoint_Throws()
    {
        var service = CreateService();

        Assert.Throws<ArgumentException>(() =>
            service.RegisterNode("node-a", new Uri("http://router.example"), "secret-a", 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SelectRouterNode_HealthyNode_ReturnsLeaseAndIncrementsLoad()
    {
        var service = CreateService();
        var node = service.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        service.ApplyHealth(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        var lease = service.SelectRouterNode("req-1", DateTimeOffset.UtcNow);

        Assert.Equal(node.Id, lease.NodeId);
        var summary = Assert.Single(service.ListRouterNodes());
        Assert.Equal(1, summary.InFlightRequests);
    }

    [Fact]
    public void SelectRouterNode_UnhealthyNode_Throws()
    {
        var service = CreateService();
        service.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => service.SelectRouterNode("req-1", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SelectRouterNode_ChoosesLeastLoadedHealthyNode()
    {
        var service = CreateService();
        var nodeA = service.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var nodeB = service.RegisterNode("node-b", new Uri("https://router-b.example"), "secret-b", 1, DateTimeOffset.UtcNow);
        service.ApplyHealth(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        service.ApplyHealth(nodeB.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        var first = service.SelectRouterNode("req-1", DateTimeOffset.UtcNow);
        var second = service.SelectRouterNode("req-2", DateTimeOffset.UtcNow);

        Assert.NotEqual(first.NodeId, second.NodeId);
    }

    [Fact]
    public void DisableNode_RemovesNodeFromSelection()
    {
        var service = CreateService();
        var nodeA = service.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var nodeB = service.RegisterNode("node-b", new Uri("https://router-b.example"), "secret-b", 1, DateTimeOffset.UtcNow);
        service.ApplyHealth(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        service.ApplyHealth(nodeB.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        service.DisableNode(nodeA.Id);
        var lease = service.SelectRouterNode("req-1", DateTimeOffset.UtcNow);

        Assert.Equal(nodeB.Id, lease.NodeId);
    }

    [Fact]
    public void ReleaseRouterNode_DecrementsLoad()
    {
        var service = CreateService();
        var node = service.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        service.ApplyHealth(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        var lease = service.SelectRouterNode("req-1", DateTimeOffset.UtcNow);

        service.ReleaseRouterNode(lease.LeaseId);

        var summary = Assert.Single(service.ListRouterNodes());
        Assert.Equal(0, summary.InFlightRequests);
    }

    [Fact]
    public void ReleaseExpiredLeases_DecrementsLoad()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var node = service.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        service.ApplyHealth(node.Id, RouterNodeStatus.Healthy, now);
        service.SelectRouterNode("req-1", now);

        service.ReleaseExpiredLeases(now.AddMinutes(6));

        var summary = Assert.Single(service.ListRouterNodes());
        Assert.Equal(0, summary.InFlightRequests);
    }

    private static RouterControlPlaneService CreateService()
    {
        return new RouterControlPlaneService(new InMemoryControlPlaneStore());
    }
}

