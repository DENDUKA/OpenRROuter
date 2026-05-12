using System.Text;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Router;

/// <summary>
/// HTTP adapter for the internal 9Router contract.
/// </summary>
public sealed class HttpRouterProxyClient(HttpClient httpClient, IRouterNodeTokenProvider tokenProvider)
    : IRouterProxyClient
{
    private const string NodeTokenHeader = "X-ORR-Node-Token";

    /// <inheritdoc />
    public async Task<RouterProxyResponse> SendChatCompletionAsync(
        GatewayRouterLease lease,
        string requestJson,
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildEndpoint(lease.EndpointUrl, "/v1/chat/completions"));
        request.Headers.Add(NodeTokenHeader, tokenProvider.Resolve(lease.TokenSecretName));
        request.Headers.Add(CorrelationIds.HeaderName, requestId);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

        return new RouterProxyResponse((int)response.StatusCode, contentType, body);
    }

    private static Uri BuildEndpoint(Uri baseEndpoint, string path)
    {
        var builder = new UriBuilder(baseEndpoint)
        {
            Path = path.TrimStart('/')
        };

        return builder.Uri;
    }
}
