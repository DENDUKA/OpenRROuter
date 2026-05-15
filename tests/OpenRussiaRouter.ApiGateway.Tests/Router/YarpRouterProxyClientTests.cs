using System.Net;
using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Telemetry;
using Yarp.ReverseProxy.Forwarder;

namespace OpenRussiaRouter.ApiGateway.Tests.Router;

public sealed class YarpRouterProxyClientTests
{
    [Fact]
    public async Task SendChatCompletionAsync_SendsExpectedInternalRequest()
    {
        var forwarder = new CapturingForwarder();
        var tokenProvider = new StaticTokenProvider("node-token");
        var client = new YarpRouterProxyClient(forwarder, tokenProvider);
        var lease = new GatewayRouterLease(
            "lease-1",
            "node-1",
            new Uri("https://router.example/internal"),
            "secret-a");

        var response = await client.SendChatCompletionAsync(lease, """{"model":"gpt-test"}""", "req-1", "sk-test_secret", CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("""{"ok":true}""", response.Body);
        Assert.Equal("https://router.example/", forwarder.DestinationPrefix);
        Assert.Equal("/v1/chat/completions", forwarder.Path);
        Assert.Equal("node-token", forwarder.NodeToken);
        Assert.Equal("req-1", forwarder.RequestId);
        Assert.Equal("""{"model":"gpt-test"}""", forwarder.Body);
    }

    private sealed class CapturingForwarder : IHttpForwarder
    {
        public string? DestinationPrefix { get; private set; }

        public string? Path { get; private set; }

        public string? NodeToken { get; private set; }

        public string? RequestId { get; private set; }

        public string? Body { get; private set; }

        public async ValueTask<ForwarderError> SendAsync(
            HttpContext context,
            string destinationPrefix,
            HttpMessageInvoker httpClient,
            ForwarderRequestConfig requestConfig,
            HttpTransformer transformer)
        {
            DestinationPrefix = destinationPrefix;
            Path = context.Request.Path;
            NodeToken = context.Request.Headers["X-ORR-Node-Token"];
            RequestId = context.Request.Headers[CorrelationIds.HeaderName];

            using var reader = new StreamReader(context.Request.Body);
            Body = await reader.ReadToEndAsync(context.RequestAborted);

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"ok":true}""", context.RequestAborted);

            return ForwarderError.None;
        }
    }

    private sealed class StaticTokenProvider(string token) : IRouterNodeTokenProvider
    {
        public string Resolve(string tokenSecretName) => token;
    }
}

