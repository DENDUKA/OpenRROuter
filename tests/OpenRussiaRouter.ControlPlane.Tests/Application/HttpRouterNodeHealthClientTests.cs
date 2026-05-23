using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class HttpRouterNodeHealthClientTests
{
    [Fact]
    public async Task CheckAsync_ConfiguredToken_SendsTokenAndParsesStatus()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"degraded"}""")
        });
        var client = CreateClient(handler);
        var node = CreateNode("node-token");

        var result = await client.CheckAsync(node, CancellationToken.None);

        result.Status.Should().Be(RouterNodeStatus.Degraded);
        handler.LastRequest?.Headers.GetValues("X-ORR-Node-Token").Single().Should().Be("node-token");
        handler.LastRequest?.RequestUri?.ToString().Should().Be("https://router.example/healthz");
    }

    [Fact]
    public async Task CheckAsync_HttpFailure_ReturnsStableErrorCode()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = CreateClient(handler);
        var node = CreateNode("node-token");

        var result = await client.CheckAsync(node, CancellationToken.None);

        result.Status.Should().Be(RouterNodeStatus.Unhealthy);
        result.ErrorCode.Should().Be("http_503");
    }

    private static HttpRouterNodeHealthClient CreateClient(HttpMessageHandler handler)
    {
        var options = TestOptionsMonitor.Create(new RouterHealthCheckOptions
        {
            HealthPath = "/healthz",
            NodeTokenHeader = "X-ORR-Node-Token",
            Timeout = TimeSpan.FromSeconds(5)
        });
        return new HttpRouterNodeHealthClient(new HttpClient(handler), options);
    }

    private static RouterNode CreateNode(string controlToken = "secret-a")
    {
        return new RouterNode(
            "node-a",
            "node-a",
            new Uri("https://router.example"),
            controlToken,
            1,
            DateTimeOffset.UtcNow);
    }

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }
}
