using System.Text.Json;
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
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), CatalogModels([]), store, new QuotaSnapshotStore(),
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
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), CatalogModels([]), store, new QuotaSnapshotStore(),
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
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), CatalogModels([]), store, new QuotaSnapshotStore(),
            Options.Create(options).ToMonitor(), NullLogger<NodePollingWorker>.Instance, time);

        await worker.RunOnceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        await worker.RunOnceAsync(CancellationToken.None);

        nodes.Verify(n => n.GetNodesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        store.Current.Nodes.Should().ContainSingle(n => n.NodeId == "n1");
    }

    [Fact]
    public async Task RunOnceAsync_BuildsModelCatalog_WithBillingStatePerModel()
    {
        var quotaStore = new QuotaSnapshotStore();
        var worker = BuildWorkerWithCatalog(quotaStore);

        await worker.RunOnceAsync(CancellationToken.None);

        var catalog = quotaStore.Current.ModelCatalog;
        catalog.Should().HaveCount(3);
        catalog.Should().ContainSingle(e => e.ModelId == "RWT/On")
            .Which.Should().BeEquivalentTo(new
            {
                NodeId = "n1",
                Provider = "chutes",
                ConnectionId = "c1",
                RouterModelId = "m-on",
                BillingState = ModelBillingStates.Enabled
            });
        catalog.Should().ContainSingle(e => e.ModelId == "RWT/Off")
            .Which.BillingState.Should().Be(ModelBillingStates.Disabled);
        catalog.Should().ContainSingle(e => e.ModelId == "m-unknown")
            .Which.Should().BeEquivalentTo(new
            {
                RouterModelId = "m-unknown",
                BillingState = ModelBillingStates.Absent
            });
    }

    [Fact]
    public async Task RunOnceAsync_KeepsDisabledModels_OutOfActiveModelMeasurements()
    {
        var quotaStore = new QuotaSnapshotStore();
        var worker = BuildWorkerWithCatalog(quotaStore);

        await worker.RunOnceAsync(CancellationToken.None);

        quotaStore.Current.ActiveModelMeasurements
            .Should().ContainSingle()
            .Which.ModelId.Should().Be("RWT/On");
    }

    /// <summary>Нода отдаёт три модели: включённую у нас, выключенную и вовсе неизвестную каталогу.</summary>
    private NodePollingWorker BuildWorkerWithCatalog(QuotaSnapshotStore quotaStore)
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections =
                [
                    new NodeProviderWithModels
                    {
                        Id = "c1",
                        Provider = "chutes",
                        IsActive = true,
                        Models =
                        [
                            new NodeProviderModelInfo { Id = "m-on", PublicId = "m-on", Enabled = true },
                            new NodeProviderModelInfo { Id = "m-off", PublicId = "m-off", Enabled = true },
                            new NodeProviderModelInfo { Id = "m-unknown", PublicId = "m-unknown", Enabled = true }
                        ]
                    }
                ]
            });
        client.Setup(c => c.ListCombosAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeCombosResponse());
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers =
                [
                    new NodeProviderQuotaEntry
                    {
                        ConnectionId = "c1",
                        Provider = "chutes",
                        Name = "chutes-main",
                        IsActive = true,
                        QuotaSupported = true,
                        Usage = JsonSerializer.Deserialize<JsonElement>(
                            """{"quotas":{"daily":{"remaining":50,"total":100,"remainingPercentage":50}}}""")
                    }
                ]
            });

        var billingModels = CatalogModels(
        [
            new BillingCatalogModel("RWT/On", "m-on", true),
            new BillingCatalogModel("RWT/Off", "m-off", false)
        ]);

        return new NodePollingWorker(
            SuccessfulNodeList().Object, Collector(client.Object), QuotaCollector(client.Object), billingModels,
            new CollectorSnapshotStore(), quotaStore,
            Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);
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
        new(nodes, Collector(client), QuotaCollector(client), CatalogModels([]), new CollectorSnapshotStore(), new QuotaSnapshotStore(),
            Options.Create(options).ToMonitor(), NullLogger<NodePollingWorker>.Instance, time);

    private static IBillingModelCatalogProvider CatalogModels(IReadOnlyList<BillingCatalogModel> models) =>
        new TestBillingModelCatalogProvider(models);

    private sealed class TestBillingModelCatalogProvider(IReadOnlyList<BillingCatalogModel> models)
        : IBillingModelCatalogProvider
    {
        public Task<IReadOnlyList<BillingCatalogModel>> ListModels(CancellationToken cancellationToken) =>
            Task.FromResult(models);
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
