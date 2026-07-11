using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;

namespace OpenRussiaRouter.CollectorService.Tests.Collection;

public class NodeCatalogCollectorTests
{
    private static CollectorNode Node() =>
        new("n1", new Uri("http://node/"), "tok", Enabled: true, Status: "Healthy", HasBeenHealthChecked: true);

    private static NodeCatalogCollector Build(INodeAdminClient client) =>
        new(client, Options.Create(new CollectorOptions()).ToMonitor(), NullLogger<NodeCatalogCollector>.Instance);

    [Fact]
    public async Task CollectAsync_MapsActiveEnabledModels_AsRank2()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections =
                [
                    new NodeProviderWithModels
                    {
                        Id = "c1", IsActive = true,
                        Models = [new NodeProviderModelInfo { Id = "gpt", PublicId = "gpt-pub", Enabled = true, Type = "text" }]
                    }
                ]
            });
        client.Setup(c => c.ListCombosAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeCombosResponse());

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Unreachable.Should().BeFalse();
        result.Observations.Should().ContainSingle();
        result.Observations[0].PublicId.Should().Be("gpt-pub");
        result.Observations[0].Rank.Should().Be(2);
    }

    [Fact]
    public async Task CollectAsync_FallsBackToV1Models_On404()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NodeAdminException(HttpStatusCode.NotFound, "404"));
        client.Setup(c => c.ListModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeModelsResponse { Data = [new NodeModelInfo { Id = "fallback" }] });
        client.Setup(c => c.ListCombosAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeCombosResponse());
        client.Setup(c => c.DescribeListModelsRequest(It.IsAny<Uri>())).Returns("GET http://node/v1/models");

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Unreachable.Should().BeFalse();
        result.Observations.Should().ContainSingle(o => o.UpstreamId == "fallback" && o.Rank == 1);
    }

    [Fact]
    public async Task CollectAsync_AddsComboAlias_AsRoutableObservation()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections =
                [
                    new NodeProviderWithModels
                    {
                        Id = "c1", IsActive = true,
                        Models = [new NodeProviderModelInfo { Id = "deepseek", PublicId = "ocg/deepseek-v4-flash", Enabled = true }]
                    }
                ]
            });
        client.Setup(c => c.ListCombosAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeCombosResponse
            {
                Combos = [new NodeComboInfo { Name = "deepseek-v4-flash", Models = ["ocg/deepseek-v4-flash"] }]
            });

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Unreachable.Should().BeFalse();
        result.Observations.Should().Contain(o =>
            o.UpstreamId == "deepseek-v4-flash" &&
            o.PublicId == "deepseek-v4-flash" &&
            o.DisplayName == "deepseek-v4-flash" &&
            o.Rank == 3);
    }

    [Fact]
    public async Task CollectAsync_KeepsProviderCatalog_WhenComboEndpointFails()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections =
                [
                    new NodeProviderWithModels
                    {
                        Id = "c1", IsActive = true,
                        Models = [new NodeProviderModelInfo { Id = "deepseek", PublicId = "ocg/deepseek-v4-flash", Enabled = true }]
                    }
                ]
            });
        client.Setup(c => c.ListCombosAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NodeAdminException(HttpStatusCode.NotFound, "missing"));

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Unreachable.Should().BeFalse();
        result.Observations.Should().ContainSingle(o => o.PublicId == "ocg/deepseek-v4-flash");
    }

    [Fact]
    public async Task CollectAsync_MarksUnreachable_OnTransientError()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NodeAdminException(HttpStatusCode.InternalServerError, "boom"));
        client.Setup(c => c.DescribeListProvidersRequest(It.IsAny<Uri>())).Returns("GET http://node/api/integrations/providers/with-models");

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Unreachable.Should().BeTrue();
        result.Response.Should().Contain("HTTP 500");
    }

    [Theory]
    [InlineData(true, "Healthy", true, true)]
    [InlineData(true, "Unhealthy", true, false)]
    [InlineData(true, "Unknown", false, true)]
    [InlineData(false, "Healthy", true, false)]
    public void IsRoutableForCatalog(bool enabled, string status, bool checkedBefore, bool expected)
    {
        var node = new CollectorNode("n", new Uri("http://n/"), "t", enabled, status, checkedBefore);
        NodeCatalogCollector.IsRoutableForCatalog(node).Should().Be(expected);
    }
}

internal static class OptionsTestExtensions
{
    public static IOptionsMonitor<T> ToMonitor<T>(this IOptions<T> options) where T : class => new StaticMonitor<T>(options.Value);

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T> where T : class
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
