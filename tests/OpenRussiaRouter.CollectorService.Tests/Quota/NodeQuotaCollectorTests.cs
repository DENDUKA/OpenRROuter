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
        result[0].Should().Be(new QuotaMeasurement("n1", "openai", "c1", 1234.5, 50d, []));
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
