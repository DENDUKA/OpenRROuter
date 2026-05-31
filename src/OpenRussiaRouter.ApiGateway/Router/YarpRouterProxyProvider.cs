using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Telemetry;
using Yarp.ReverseProxy.Forwarder;

namespace OpenRussiaRouter.ApiGateway.Router;

public sealed class YarpRouterProxyProvider(IHttpForwarder forwarder)
    : IRouterProxyProvider
{
    private const string ChatCompletionsPath = "/v1/chat/completions";

    private static readonly HttpMessageInvoker ForwardingHttpClient = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        EnableMultipleHttp2Connections = true,
        ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
        ConnectTimeout = TimeSpan.FromSeconds(15)
    });

    private static readonly HttpClient StreamingHttpClient = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        EnableMultipleHttp2Connections = true,
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
        string userApiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = lease.EndpointUrl.Scheme;
        context.Request.Host = HostString.FromUriComponent(lease.EndpointUrl);
        context.Request.Path = ChatCompletionsPath;
        context.Request.Headers["Authorization"] = "Bearer " + userApiKey;
        context.Request.Headers[CorrelationIds.HeaderName] = requestId;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Request.ContentLength = context.Request.Body.Length;
        context.RequestAborted = cancellationToken;

        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var destinationPrefix = BuildDestinationPrefix(lease.EndpointUrl);
        var error = await forwarder.SendAsync(context, destinationPrefix, ForwardingHttpClient, RequestConfig).ConfigureAwait(false);
        if (error != ForwarderError.None)
        {
            var errorFeature = context.GetForwarderErrorFeature();
            throw new InvalidOperationException("YARP forwarding failed.", errorFeature?.Exception);
        }

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var contentType = context.Response.ContentType ?? "application/json";

        var providerName = context.Response.Headers.TryGetValue("x-9r-provider", out var pv) ? pv.ToString() : null;
        var providerAccount = context.Response.Headers.TryGetValue("x-9r-account", out var av) ? av.ToString() : null;

        return new RouterProxyResponse(context.Response.StatusCode, contentType, body, providerName, providerAccount);
    }

    public async Task<RouterStreamSession> BeginStreamChatCompletionAsync(
        GatewayRouterLease lease,
        string requestJson,
        string requestId,
        string userApiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        var requestUri = new Uri(lease.EndpointUrl, ChatCompletionsPath);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + userApiKey);
        request.Headers.TryAddWithoutValidation(CorrelationIds.HeaderName, requestId);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response = await StreamingHttpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        return new RouterStreamSession(response);
    }

    private static string BuildDestinationPrefix(Uri baseEndpoint)
    {
        return new Uri(baseEndpoint.GetLeftPart(UriPartial.Authority)).AbsoluteUri;
    }
}
