using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Telemetry;
using Yarp.ReverseProxy.Forwarder;

namespace OpenRussiaRouter.ApiGateway.Router;

public sealed class YarpRouterProxyClient(IHttpForwarder forwarder, IRouterNodeTokenProvider tokenProvider)
    : IRouterProxyClient
{
    private const string NodeTokenHeader = "X-ORR-Node-Token";
    private static readonly HttpMessageInvoker HttpClient = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        EnableMultipleHttp2Connections = true,
        ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
        ConnectTimeout = TimeSpan.FromSeconds(15)
    });

    private static readonly ForwarderRequestConfig RequestConfig = new()
    {
        ActivityTimeout = TimeSpan.FromMinutes(5)
    };

    public async Task<RouterProxyResponse> SendChatCompletionAsync(
        GatewayRouterLease lease,
        string requestJson,
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = lease.EndpointUrl.Scheme;
        context.Request.Host = HostString.FromUriComponent(lease.EndpointUrl);
        context.Request.Path = "/v1/chat/completions";
        context.Request.Headers[NodeTokenHeader] = tokenProvider.Resolve(lease.TokenSecretName);
        context.Request.Headers[CorrelationIds.HeaderName] = requestId;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Request.ContentLength = context.Request.Body.Length;
        context.RequestAborted = cancellationToken;

        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var destinationPrefix = BuildDestinationPrefix(lease.EndpointUrl);
        var error = await forwarder.SendAsync(context, destinationPrefix, HttpClient, RequestConfig).ConfigureAwait(false);
        if (error != ForwarderError.None)
        {
            var errorFeature = context.GetForwarderErrorFeature();
            throw new InvalidOperationException("YARP forwarding failed.", errorFeature?.Exception);
        }

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var contentType = context.Response.ContentType ?? "application/json";

        return new RouterProxyResponse(context.Response.StatusCode, contentType, body);
    }

    private static string BuildDestinationPrefix(Uri baseEndpoint)
    {
        return new Uri(baseEndpoint.GetLeftPart(UriPartial.Authority)).AbsoluteUri;
    }
}
