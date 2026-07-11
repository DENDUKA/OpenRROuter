using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.CollectorService.Application.Billing;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;
using OpenRussiaRouter.CollectorService.Application.Quota;

namespace OpenRussiaRouter.CollectorService.Tests.Collection;

public class NodePollingWorkerTests
{
    private static NodeCatalogCollector Collector(INodeAdminClient client) =>
        new(client, Options.Create(new CollectorOptions()).ToMonitor(), NullLogger<NodeCatalogCollector>.Instance);

    private static NodeQuotaCollector QuotaCollector(INodeAdminClient client) =>
        new(client, Options.Create(new CollectorOptions()).ToMonitor(), NullLogger<NodeQuotaCollector>.Instance);

    [Fact]
    public async Task RunOnceAsync_PollsRoutableNodes_AndSwapsSnapshot()
    {
        var nodes = new Mock<INodeListProvider>();
        nodes.Setup(n => n.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectorNode>
            {
                new("n1", new Uri("http://n1/"), "t", true, "Healthy", true)
            });

        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections = [new NodeProviderWithModels { Id = "c1", IsActive = true, Models = [new NodeProviderModelInfo { Id = "m", PublicId = "m", Enabled = true }] }]
            });
        client.Setup(c => c.ListCombosAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeCombosResponse());
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse());

        var store = new CollectorSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), ActiveModels([]), store, new QuotaSnapshotStore(),
            Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);


        await worker.RunOnceAsync(CancellationToken.None);

        store.Current.Nodes.Should().ContainSingle(n => n.NodeId == "n1");
        store.Current.CollectedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnceAsync_KeepsPreviousSnapshot_WhenNodeListFails()
    {
        var nodes = new Mock<INodeListProvider>();
        nodes.Setup(n => n.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("control plane down"));

        var store = new CollectorSnapshotStore();
        var previous = new CollectorSnapshot([new NodeCatalogResult("old", false, [], null, null)], DateTimeOffset.UnixEpoch);
        store.Set(previous);

        var client = new Mock<INodeAdminClient>();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), ActiveModels([]), store, new QuotaSnapshotStore(),
            Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);


        await worker.RunOnceAsync(CancellationToken.None);

        store.Current.Should().BeSameAs(previous);
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotRefetchNodeList_WithinRefreshInterval()
    {
        var nodes = SuccessfulNodeList();
        var client = SuccessfulNodeClient();
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var options = new CollectorOptions { NodeListRefreshInterval = TimeSpan.FromMinutes(5) };
        var worker = BuildWorker(nodes.Object, client.Object, options, time);

        await worker.RunOnceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        await worker.RunOnceAsync(CancellationToken.None);

        nodes.Verify(n => n.GetNodesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_RefetchesNodeList_AfterRefreshInterval()
    {
        var nodes = SuccessfulNodeList();
        var client = SuccessfulNodeClient();
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var options = new CollectorOptions { NodeListRefreshInterval = TimeSpan.FromMinutes(5) };
        var worker = BuildWorker(nodes.Object, client.Object, options, time);

        await worker.RunOnceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        await worker.RunOnceAsync(CancellationToken.None);

        nodes.Verify(n => n.GetNodesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunOnceAsync_ReusesStaleCache_WhenRefreshFails()
    {
        var nodes = new Mock<INodeListProvider>();
        nodes.SetupSequence(n => n.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectorNode>
            {
                new("n1", new Uri("http://n1/"), "t", true, "Healthy", true)
            })
            .ThrowsAsync(new InvalidOperationException("control plane down"));

        var client = SuccessfulNodeClient();
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var options = new CollectorOptions { NodeListRefreshInterval = TimeSpan.FromMinutes(5) };
        var store = new CollectorSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), ActiveModels([]), store, new QuotaSnapshotStore(),
            Options.Create(options).ToMonitor(), NullLogger<NodePollingWorker>.Instance, time);

        await worker.RunOnceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        await worker.RunOnceAsync(CancellationToken.None);

        nodes.Verify(n => n.GetNodesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        store.Current.Nodes.Should().ContainSingle(n => n.NodeId == "n1");
    }

    private static Mock<INodeListProvider> SuccessfulNodeList()
    {
        var nodes = new Mock<INodeListProvider>();
        nodes.Setup(n => n.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectorNode>
            {
                new("n1", new Uri("http://n1/"), "t", true, "Healthy", true)
            });
        return nodes;
    }

    private static Mock<INodeAdminClient> SuccessfulNodeClient()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections = [new NodeProviderWithModels { Id = "c1", IsActive = true, Models = [new NodeProviderModelInfo { Id = "m", PublicId = "m", Enabled = true }] }]
            });
        client.Setup(c => c.ListCombosAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeCombosResponse());
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse());
        return client;
    }

    private NodePollingWorker BuildWorker(
        INodeListProvider nodes, INodeAdminClient client, CollectorOptions options, TimeProvider time) =>
        new(nodes, Collector(client), QuotaCollector(client), ActiveModels([]), new CollectorSnapshotStore(), new QuotaSnapshotStore(),
            Options.Create(options).ToMonitor(), NullLogger<NodePollingWorker>.Instance, time);

    private static IActiveBillingModelProvider ActiveModels(IReadOnlyList<ActiveBillingModel> models) =>
        new TestActiveBillingModelProvider(models);

    private sealed class TestActiveBillingModelProvider(IReadOnlyList<ActiveBillingModel> models) : IActiveBillingModelProvider
    {
        public Task<IReadOnlyList<ActiveBillingModel>> ListActiveModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(models);
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
