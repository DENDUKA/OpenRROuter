using System.Net;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Tests.Router;

public sealed class HttpRouterProxyClientTests
{
    [Fact]
    public async Task SendChatCompletionAsync_SendsExpectedInternalRequest()
    {
        using var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var tokenProvider = new StaticTokenProvider("node-token");
        var client = new HttpRouterProxyClient(httpClient, tokenProvider);
        var lease = new GatewayRouterLease(
            "lease-1",
            "node-1",
            new Uri("https://router.example/internal"),
            "secret-a");

        var response = await client.SendChatCompletionAsync(lease, """{"model":"gpt-test"}""", "req-1", CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("""{"ok":true}""", response.Body);
        Assert.Equal(new Uri("https://router.example/v1/chat/completions"), handler.Request!.RequestUri);
        Assert.Equal("node-token", handler.Request.Headers.GetValues("X-ORR-Node-Token").Single());
        Assert.Equal("req-1", handler.Request.Headers.GetValues(CorrelationIds.HeaderName).Single());
        Assert.Equal("""{"model":"gpt-test"}""", handler.Body);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""")
            };
        }
    }

    private sealed class StaticTokenProvider(string token) : IRouterNodeTokenProvider
    {
        public string Resolve(string tokenSecretName) => token;
    }
}

