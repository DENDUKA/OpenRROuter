using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), store, quotaStore,
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
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), store, quotaStore,
            Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);

        await worker.RunOnceAsync(CancellationToken.None);

        store.Current.Should().BeSameAs(previous);
    }
}
