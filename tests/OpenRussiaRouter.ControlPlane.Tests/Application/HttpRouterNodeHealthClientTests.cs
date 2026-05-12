using System.Net;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application;
using OpenRussiaRouter.ControlPlane.Domain;

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
        var client = CreateClient(handler, new StaticTokenResolver("node-token"));
        var node = CreateNode();

        var result = await client.CheckAsync(node, CancellationToken.None);

        Assert.Equal(RouterNodeStatus.Degraded, result.Status);
        Assert.Equal("node-token", handler.LastRequest?.Headers.GetValues("X-ORR-Node-Token").Single());
        Assert.Equal("https://router.example/healthz", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task CheckAsync_MissingToken_ReturnsUnhealthyWithoutRequest()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler, new StaticTokenResolver(null));
        var node = CreateNode();

        var result = await client.CheckAsync(node, CancellationToken.None);

        Assert.Equal(RouterNodeStatus.Unhealthy, result.Status);
        Assert.Equal("node_token_missing", result.ErrorCode);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CheckAsync_HttpFailure_ReturnsStableErrorCode()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = CreateClient(handler, new StaticTokenResolver("node-token"));
        var node = CreateNode();

        var result = await client.CheckAsync(node, CancellationToken.None);

        Assert.Equal(RouterNodeStatus.Unhealthy, result.Status);
        Assert.Equal("http_503", result.ErrorCode);
    }

    private static HttpRouterNodeHealthClient CreateClient(
        HttpMessageHandler handler,
        IRouterNodeTokenResolver tokenResolver)
    {
        var options = TestOptionsMonitor.Create(new RouterHealthCheckOptions
        {
            HealthPath = "/healthz",
            NodeTokenHeader = "X-ORR-Node-Token",
            Timeout = TimeSpan.FromSeconds(5)
        });
        return new HttpRouterNodeHealthClient(new HttpClient(handler), tokenResolver, options);
    }

    private static RouterNode CreateNode()
    {
        return new RouterNode(
            "node-a",
            "node-a",
            new Uri("https://router.example"),
            "secret-a",
            1,
            DateTimeOffset.UtcNow);
    }

    private sealed class StaticTokenResolver(string? token) : IRouterNodeTokenResolver
    {
        public string? Resolve(string tokenSecretName) => token;
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
