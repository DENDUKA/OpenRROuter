using System.Net;
using System.Text;

namespace OpenRussiaRouter.WebApp.Services.ControlPlane;

public sealed class RouterNodeApiProvider(IHttpClientFactory httpClientFactory)
{
    private const string ClientName = "RouterNode";
    private const string ChatCompletionsPath = "/v1/chat/completions";
    private const string ProvidersPath = "/api/providers";

    public async Task<RouterNodeApiResponse> SendChatCompletionAsync(
        RouterNodeInfo node,
        string bearerToken,
        string requestJson,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(node, bearerToken, requestJson);
        using var response = await CreateClient().SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return new RouterNodeApiResponse(response.StatusCode, response.IsSuccessStatusCode, body);
    }

    public async Task<HttpResponseMessage> SendStreamingChatCompletionAsync(
        RouterNodeInfo node,
        string bearerToken,
        string requestJson,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(node, bearerToken, requestJson);
        return await CreateClient()
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RouterNodeProbeResult> ProbeProvidersAsync(
        RouterNodeInfo node,
        CancellationToken cancellationToken)
    {
        var requestLine = DescribeProvidersRequest(node);

        if (string.IsNullOrWhiteSpace(node.ControlToken))
        {
            return new RouterNodeProbeResult(
                requestLine,
                "Control token is empty in OpenRussiaRouter DB - open this node in /admin/nodes and re-create it with the Control Token from 9Router's .env (ROUTER_CONTROL_TOKEN).");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(node, ProvidersPath));
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + node.ControlToken);
            request.Headers.TryAddWithoutValidation("X-ORR-Node-Token", node.ControlToken);
            using var response = await CreateClient().SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            const int maxBody = 1024;
            if (body.Length > maxBody)
            {
                body = body[..maxBody] + "... (truncated)";
            }

            return new RouterNodeProbeResult(requestLine, $"HTTP {(int)response.StatusCode} {response.StatusCode}\n{body}");
        }
        catch (Exception ex)
        {
            return new RouterNodeProbeResult(requestLine, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private string DescribeProvidersRequest(RouterNodeInfo node) => $"GET {BuildUri(node, ProvidersPath)}";

    private HttpClient CreateClient() => httpClientFactory.CreateClient(ClientName);

    private static HttpRequestMessage CreateJsonRequest(
        RouterNodeInfo node,
        string bearerToken,
        string requestJson)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(node, ChatCompletionsPath));
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        return request;
    }

    private static Uri BuildUri(RouterNodeInfo node, string path)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (string.IsNullOrWhiteSpace(node.EndpointUrl))
        {
            throw new InvalidOperationException("Router node endpoint URL is empty.");
        }

        return new Uri(node.EndpointUrl.TrimEnd('/') + path);
    }
}

public sealed record RouterNodeApiResponse(HttpStatusCode StatusCode, bool IsSuccessStatusCode, string Body);

public sealed record RouterNodeProbeResult(string RequestLine, string ResponseLine);
