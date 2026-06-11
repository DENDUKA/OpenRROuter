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

        using var usage = System.Text.Json.JsonDocument.Parse("""{"remaining": 7}""");
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers = [new NodeProviderQuotaEntry { ConnectionId = "c1", Provider = "openai", Usage = usage.RootElement.Clone() }]
            });

        var store = new CollectorSnapshotStore();
        var quotaStore = new QuotaSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), ActiveModels([]), store, quotaStore,
            Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);

        await worker.RunOnceAsync(CancellationToken.None);

        store.Current.Nodes.Should().ContainSingle(n => n.NodeId == "n1");
        store.Current.CollectedAtUtc.Should().NotBeNull();
        quotaStore.Current.Measurements.Should().ContainSingle(m => m.NodeId == "n1" && m.Remaining == 7d);
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
        var quotaStore = new QuotaSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), ActiveModels([]), store, quotaStore,
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
        time.Advance(TimeSpan.FromMinutes(1)); // still within the 5-minute refresh window
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
        time.Advance(TimeSpan.FromMinutes(5)); // reaches the refresh threshold
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
        var quotaStore = new QuotaSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), ActiveModels([]), store, quotaStore,
            Options.Create(options).ToMonitor(), NullLogger<NodePollingWorker>.Instance, time);

        await worker.RunOnceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5)); // forces a refresh that will fail
        await worker.RunOnceAsync(CancellationToken.None);

        nodes.Verify(n => n.GetNodesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        // Stale cache is reused, so the snapshot is rebuilt from "n1" instead of being frozen.
        store.Current.Nodes.Should().ContainSingle(n => n.NodeId == "n1");
    }

    [Fact]
    public async Task RunOnceAsync_PublishesActiveModelQuota_FromNestedModelQuota()
    {
        var nodes = SuccessfulNodeList();
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections =
                [
                    new NodeProviderWithModels
                    {
                        Id = "c1",
                        Provider = "openai",
                        IsActive = true,
                        Models =
                        [
                            new NodeProviderModelInfo { Id = "router/gpt", PublicId = "router/gpt", Enabled = true },
                            new NodeProviderModelInfo { Id = "router/disabled", PublicId = "router/disabled", Enabled = true }
                        ]
                    }
                ]
            });

        using var usage = System.Text.Json.JsonDocument.Parse("""
        {
            "quotas": {
                "router/gpt": { "remaining": 8, "total": 10, "remainingPercentage": 80 },
                "router/disabled": { "remaining": 1, "total": 10, "remainingPercentage": 10 }
            }
        }
        """);
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers = [new NodeProviderQuotaEntry { ConnectionId = "c1", Provider = "quota-openai", Usage = usage.RootElement.Clone() }]
            });

        var quotaStore = new QuotaSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object),
            ActiveModels([new ActiveBillingModel("ORR/GPT", "router/gpt")]),
            new CollectorSnapshotStore(), quotaStore, Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);

        await worker.RunOnceAsync(CancellationToken.None);

        quotaStore.Current.ActiveModelMeasurements.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ActiveModelQuotaMeasurement(
                "n1", "openai", "c1", "ORR/GPT", "router/gpt", 80d));
    }

    [Fact]
    public async Task RunOnceAsync_AppliesFlatProviderQuota_ToAllActiveModelsOnConnection()
    {
        var nodes = SuccessfulNodeList();
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections =
                [
                    new NodeProviderWithModels
                    {
                        Id = "c1",
                        Provider = "gemini",
                        IsActive = true,
                        Models =
                        [
                            new NodeProviderModelInfo { Id = "router/a", PublicId = "router/a", Enabled = true },
                            new NodeProviderModelInfo { Id = "router/b", PublicId = "router/b", Enabled = true },
                            new NodeProviderModelInfo { Id = "router/not-admin", PublicId = "router/not-admin", Enabled = true }
                        ]
                    }
                ]
            });

        using var usage = System.Text.Json.JsonDocument.Parse("""{"remaining": 25,"limit": 100}""");
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers = [new NodeProviderQuotaEntry { ConnectionId = "c1", Provider = "gemini-quota", Usage = usage.RootElement.Clone() }]
            });

        var quotaStore = new QuotaSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object),
            ActiveModels([
                new ActiveBillingModel("ORR/A", "router/a"),
                new ActiveBillingModel("ORR/B", "router/b")
            ]),
            new CollectorSnapshotStore(), quotaStore, Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);

        await worker.RunOnceAsync(CancellationToken.None);

        quotaStore.Current.ActiveModelMeasurements.Should().HaveCount(2);
        quotaStore.Current.ActiveModelMeasurements.Should().OnlyContain(m => m.Percent == 25d && m.Provider == "gemini");
        quotaStore.Current.ActiveModelMeasurements.Select(m => m.ModelId).Should().BeEquivalentTo("ORR/A", "ORR/B");
    }

    [Fact]
    public async Task RunOnceAsync_PublishesSeparateSeries_ForSameActiveModelOnTwoNodes()
    {
        var nodes = new Mock<INodeListProvider>();
        nodes.Setup(n => n.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectorNode>
            {
                new("n1", new Uri("http://n1/"), "t", true, "Healthy", true),
                new("n2", new Uri("http://n2/"), "t", true, "Healthy", true)
            });

        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections =
                [
                    new NodeProviderWithModels
                    {
                        Id = "c1",
                        Provider = "openai",
                        IsActive = true,
                        Models = [new NodeProviderModelInfo { Id = "router/gpt", PublicId = "router/gpt", Enabled = true }]
                    }
                ]
            });

        using var usage = System.Text.Json.JsonDocument.Parse("""{"remaining": 50,"limit": 100}""");
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers = [new NodeProviderQuotaEntry { ConnectionId = "c1", Provider = "openai", Usage = usage.RootElement.Clone() }]
            });

        var quotaStore = new QuotaSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object),
            ActiveModels([new ActiveBillingModel("ORR/GPT", "router/gpt")]),
            new CollectorSnapshotStore(), quotaStore, Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);

        await worker.RunOnceAsync(CancellationToken.None);

        quotaStore.Current.ActiveModelMeasurements.Should().HaveCount(2);
        quotaStore.Current.ActiveModelMeasurements.Select(m => m.NodeId).Should().BeEquivalentTo("n1", "n2");
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
