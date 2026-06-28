using FluentAssertions;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain;
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

        var lease = await service.SelectRouterNodeAsync(null, "req-1", DateTimeOffset.UtcNow);

        lease.NodeId.Should().Be(node.Id);
        var summary = (await service.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(1);
    }

    [Fact]
    public async Task UpdateNodeControlToken_ExistingNode_ChangesTokenUsedForLease()
    {
        var service = CreateService();
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        await service.UpdateNodeControlTokenAsync(node.Id, "secret-b");
        var lease = await service.SelectRouterNodeAsync(null, "req-1", DateTimeOffset.UtcNow);

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
    public async Task UpdateNode_ExistingNode_ChangesEditableFields()
    {
        var service = CreateService();
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);

        await service.UpdateNodeAsync(node.Id, "node-b", new Uri("http://f9router"), "secret-b", 3);

        var updated = await service.GetRouterNodeAsync(node.Id);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("node-b");
        updated.EndpointUrl.Should().Be(new Uri("http://f9router"));
        updated.ControlToken.Should().Be("secret-b");
        updated.CapacityWeight.Should().Be(3);
    }

    [Fact]
    public async Task SelectRouterNode_UnhealthyNode_Throws()
    {
        var service = CreateService();
        await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);

        await ((Func<Task>)(() => service.SelectRouterNodeAsync(null, "req-1", DateTimeOffset.UtcNow))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SelectRouterNode_ChoosesLeastLoadedHealthyNode()
    {
        var service = CreateService();
        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var nodeB = await service.RegisterNodeAsync("node-b", new Uri("https://router-b.example"), "secret-b", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeB.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        var first = await service.SelectRouterNodeAsync(null, "req-1", DateTimeOffset.UtcNow);
        var second = await service.SelectRouterNodeAsync(null, "req-2", DateTimeOffset.UtcNow);

        first.NodeId.Should().NotBe(second.NodeId);
    }

    [Fact]
    public async Task SelectRouterNode_PreferredNodeWithMatchingControlToken_ReturnsPreferredNode()
    {
        var service = CreateService();
        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var nodeB = await service.RegisterNodeAsync("node-b", new Uri("https://router-b.example"), "secret-b", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeB.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        var lease = await service.SelectRouterNodeAsync(
            null,
            "req-1",
            DateTimeOffset.UtcNow,
            nodeB.Id,
            "secret-b");

        lease.NodeId.Should().Be(nodeB.Id);
    }

    [Fact]
    public async Task SelectRouterNode_PreferredNodeWithWrongControlToken_Throws()
    {
        var service = CreateService();
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        await ((Func<Task>)(() => service.SelectRouterNodeAsync(
                null,
                "req-1",
                DateTimeOffset.UtcNow,
                node.Id,
                "wrong-secret")))
            .Should().ThrowAsync<InvalidOperationException>();
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
        var lease = await service.SelectRouterNodeAsync(null, "req-1", DateTimeOffset.UtcNow);

        lease.NodeId.Should().Be(nodeB.Id);
    }

    [Fact]
    public async Task ReleaseRouterNode_DecrementsLoad()
    {
        var service = CreateService();
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        var lease = await service.SelectRouterNodeAsync(null, "req-1", DateTimeOffset.UtcNow);

        await service.ReleaseRouterNodeAsync(lease.LeaseId);

        var summary = (await service.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(0);
    }

    [Fact]
    public async Task ReleaseExpiredLeases_DecrementsLoad()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, now);
        await service.SelectRouterNodeAsync(null, "req-1", now);

        await service.ReleaseExpiredLeasesAsync(now.AddMinutes(6));

        var summary = (await service.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(0);
    }

    // ── Capability-aware routing ──────────────────────────────────────────────

    [Fact]
    public async Task SelectRouterNode_ModelOnAllNodes_ChoosesLeastLoad()
    {
        var store = new NodeModelCapabilityStore();
        var repo = new InMemoryControlPlaneRepository();
        var service = new RouterControlPlaneService(repo, store);

        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var nodeB = await service.RegisterNodeAsync("node-b", new Uri("https://router-b.example"), "secret-b", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeB.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        store.SetNodeModels(nodeA.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gpt-4" });
        store.SetNodeModels(nodeB.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gpt-4" });

        var first = await service.SelectRouterNodeAsync("gpt-4", "req-1", DateTimeOffset.UtcNow);
        var second = await service.SelectRouterNodeAsync("gpt-4", "req-2", DateTimeOffset.UtcNow);

        first.NodeId.Should().NotBe(second.NodeId, "least-load should distribute across both nodes");
    }

    [Fact]
    public async Task SelectRouterNode_ModelOnOneNodeOnly_AlwaysPicksThatNode()
    {
        var store = new NodeModelCapabilityStore();
        var repo = new InMemoryControlPlaneRepository();
        var service = new RouterControlPlaneService(repo, store);

        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var nodeB = await service.RegisterNodeAsync("node-b", new Uri("https://router-b.example"), "secret-b", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeB.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        store.SetNodeModels(nodeA.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "glm-4.7" });
        store.SetNodeModels(nodeB.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gpt-4" });

        var lease1 = await service.SelectRouterNodeAsync("glm-4.7", "req-1", DateTimeOffset.UtcNow);
        var lease2 = await service.SelectRouterNodeAsync("glm-4.7", "req-2", DateTimeOffset.UtcNow);

        lease1.NodeId.Should().Be(nodeA.Id);
        lease2.NodeId.Should().Be(nodeA.Id);
    }

    [Fact]
    public async Task SelectRouterNode_ModelOnNoNode_ThrowsInvalidOperation()
    {
        var store = new NodeModelCapabilityStore();
        var repo = new InMemoryControlPlaneRepository();
        var service = new RouterControlPlaneService(repo, store);

        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        store.SetNodeModels(nodeA.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gpt-4" });

        await ((Func<Task>)(() => service.SelectRouterNodeAsync("glm-4.7", "req-1", DateTimeOffset.UtcNow)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*glm-4.7*");
    }

    [Fact]
    public async Task SelectRouterNode_StoreEmpty_FallsBackToAnyHealthyNode()
    {
        // Store is empty (e.g. right after startup before first Quartz job run).
        // Must NOT throw — fall back to least-load selection ignoring model filter.
        var store = new NodeModelCapabilityStore(); // IsEmpty = true
        var repo = new InMemoryControlPlaneRepository();
        var service = new RouterControlPlaneService(repo, store);

        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);

        var lease = await service.SelectRouterNodeAsync("any-model", "req-1", DateTimeOffset.UtcNow);

        lease.NodeId.Should().Be(nodeA.Id);
    }

    [Fact]
    public async Task SelectRouterNode_ModelMatchIsCaseInsensitive()
    {
        var store = new NodeModelCapabilityStore();
        var repo = new InMemoryControlPlaneRepository();
        var service = new RouterControlPlaneService(repo, store);

        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        store.SetNodeModels(nodeA.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GLM-4.7" });

        // Request uses lowercase model id
        var lease = await service.SelectRouterNodeAsync("glm-4.7", "req-1", DateTimeOffset.UtcNow);

        lease.NodeId.Should().Be(nodeA.Id);
    }

    private static RouterControlPlaneService CreateService()
    {
        return new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
    }
}
