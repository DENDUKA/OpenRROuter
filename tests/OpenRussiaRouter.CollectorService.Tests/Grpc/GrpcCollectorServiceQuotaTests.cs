using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.OpenRouter;
using OpenRussiaRouter.CollectorService.Application.Quota;
using OpenRussiaRouter.CollectorService.Infrastructure.Grpc;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.CollectorService.Tests.Grpc;

public class GrpcCollectorServiceQuotaTests
{
    private static GrpcCollectorService Build(QuotaSnapshot quota, OpenRouterCatalogSnapshot openRouter)
    {
        var store = new CollectorSnapshotStore();
        var quotaStore = new QuotaSnapshotStore();
        quotaStore.Set(quota);
        var orStore = new OpenRouterCatalogStore();
        orStore.Set(openRouter);
        return new GrpcCollectorService(store, quotaStore, orStore);
    }

    [Fact]
    public async Task GetProviderQuota_ProjectsMeasurements()
    {
        var service = Build(
            new QuotaSnapshot([new QuotaMeasurement("n1", "openai", "c1", 99d)], DateTimeOffset.UnixEpoch),
            OpenRouterCatalogSnapshot.Empty);

        var response = await service.GetProviderQuota(new GetProviderQuotaRequest(), context: null!);

        response.Measurements.Should().ContainSingle(m => m.NodeId == "n1" && m.Remaining == 99d);
    }

    [Fact]
    public async Task GetOpenRouterCatalog_ProjectsEntriesAndAvailability()
    {
        var entry = new OpenRouterEntry("openai/gpt-4o", "GPT-4o", "OpenAI", "d", 128000, ["text"], "0.000005", "0.000015", "");
        var service = Build(QuotaSnapshot.Empty, new OpenRouterCatalogSnapshot([entry], DateTimeOffset.UnixEpoch, Available: true));

        var response = await service.GetOpenRouterCatalog(new GetOpenRouterCatalogRequest(), context: null!);

        response.Available.Should().BeTrue();
        response.Entries.Should().ContainSingle(e => e.Slug == "openai/gpt-4o" && e.ContextLength == 128000);
    }
}
