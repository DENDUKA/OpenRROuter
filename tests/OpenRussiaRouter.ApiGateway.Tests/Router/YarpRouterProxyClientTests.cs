using System.Net;
using FluentAssertions;
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
        var client = new YarpRouterProxyClient(forwarder);
        var lease = new GatewayRouterLease(
            "lease-1",
            "node-1",
            new Uri("https://router.example/internal"),
            "secret-a");

        var response = await client.SendChatCompletionAsync(lease, """{"model":"gpt-test"}""", "req-1", "sk-test_secret", CancellationToken.None);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Be("""{"ok":true}""");
        forwarder.DestinationPrefix.Should().Be("https://router.example/");
        forwarder.Path.Should().Be("/v1/chat/completions");
        forwarder.NodeToken.Should().BeNull();
        forwarder.Authorization.Should().Be("Bearer sk-test_secret");
        forwarder.RequestId.Should().Be("req-1");
        forwarder.Body.Should().Be("""{"model":"gpt-test"}""");
    }

    private sealed class CapturingForwarder : IHttpForwarder
    {
        public string? DestinationPrefix { get; private set; }

        public string? Path { get; private set; }

        public string? NodeToken { get; private set; }

        public string? Authorization { get; private set; }

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
            NodeToken = context.Request.Headers.TryGetValue("X-ORR-Node-Token", out var nodeToken)
                ? nodeToken.ToString()
                : null;
            Authorization = context.Request.Headers.Authorization;
            RequestId = context.Request.Headers[CorrelationIds.HeaderName];

            using var reader = new StreamReader(context.Request.Body);
            Body = await reader.ReadToEndAsync(context.RequestAborted);

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"ok":true}""", context.RequestAborted);

            return ForwarderError.None;
        }
    }

}
