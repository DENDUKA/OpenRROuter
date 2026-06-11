using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;
using OpenRussiaRouter.CollectorService.Application.Quota;
using OpenRussiaRouter.CollectorService.Tests.Collection; // OptionsTestExtensions

namespace OpenRussiaRouter.CollectorService.Tests.Quota;

public class NodeQuotaCollectorTests
{
    private static CollectorNode Node() =>
        new("n1", new Uri("http://node/"), "tok", true, "Healthy", true);

    private static NodeQuotaCollector Build(INodeAdminClient client) =>
        new(client, Options.Create(new CollectorOptions()).ToMonitor(), NullLogger<NodeQuotaCollector>.Instance);

    [Fact]
    public async Task CollectAsync_ExtractsRemaining_FromUsageJson()
    {
        using var doc = JsonDocument.Parse("""{"remaining": 1234.5,"limit": 2469}""");
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers =
                [
                    new NodeProviderQuotaEntry { ConnectionId = "c1", Provider = "openai", Usage = doc.RootElement.Clone() }
                ]
            });

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Should().BeEquivalentTo(new QuotaMeasurement("n1", "openai", "c1", 1234.5, 50d, []));
    }

    [Fact]
    public async Task CollectAsync_ExtractsQuotas_FromNestedUsageJson()
    {
        using var doc = JsonDocument.Parse("""
        {
            "plan": "Free",
            "quotas": {
                "model-1": { "used": 100, "total": 1000, "remainingPercentage": 90 },
                "model-2": { "used": 500, "total": 1000, "remainingPercentage": 50 }
            }
        }
        """);
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers =
                [
                    new NodeProviderQuotaEntry { ConnectionId = "c2", Provider = "gemini", Usage = doc.RootElement.Clone() }
                ]
            });

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new QuotaMeasurement("n1", "gemini", "c2", 900d, 90d, ["model-1"]));
        result[1].Should().BeEquivalentTo(new QuotaMeasurement("n1", "gemini", "c2", 500d, 50d, ["model-2"]));
    }

    [Fact]
    public async Task CollectAsync_SkipsEntriesWithoutUsage_AndSwallowsErrors()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NodeAdminException(System.Net.HttpStatusCode.InternalServerError, "boom"));

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
