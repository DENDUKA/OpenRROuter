using System.Net.Http.Json;
using System.Text.Json;

namespace OpenRussiaRouter.CollectorService.Application.NodeAdmin;

public sealed class NodeAdminClient(HttpClient httpClient) : INodeAdminClient
{
    private const string ModelsPath = "/v1/models";
    private const string CombosPath = "/api/integrations/combos";
    private const string ProvidersWithModelsPath = "/api/integrations/providers/with-models";
    private const string ProviderQuotaPath = "/api/integrations/providers/quota";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string DescribeListProvidersRequest(Uri endpoint) =>
        $"GET {new Uri(endpoint, ProvidersWithModelsPath)}";

    public string DescribeListModelsRequest(Uri endpoint) =>
        $"GET {new Uri(endpoint, ModelsPath)}";

    public string DescribeListCombosRequest(Uri endpoint) =>
        $"GET {new Uri(endpoint, CombosPath)}";

    public Task<NodeProvidersWithModelsResponse> ListProvidersWithModelsAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken) =>
        SendAsync<NodeProvidersWithModelsResponse>(endpoint, controlToken, ProvidersWithModelsPath, cancellationToken);

    public Task<NodeModelsResponse> ListModelsAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken) =>
        SendAsync<NodeModelsResponse>(endpoint, controlToken, ModelsPath, cancellationToken);

    public Task<NodeCombosResponse> ListCombosAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken) =>
        SendAsync<NodeCombosResponse>(endpoint, controlToken, CombosPath, cancellationToken);

    public Task<NodeProviderQuotaResponse> GetProviderQuotaAsync(
        Uri endpoint, string controlToken, bool includeInactive, CancellationToken cancellationToken)
    {
        var path = includeInactive ? $"{ProviderQuotaPath}?includeInactive=true" : ProviderQuotaPath;
        return SendAsync<NodeProviderQuotaResponse>(endpoint, controlToken, path, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(
        Uri endpoint, string controlToken, string relativePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (string.IsNullOrWhiteSpace(controlToken))
            throw new InvalidOperationException($"Control token is not set for endpoint '{endpoint}'.");

        var absoluteUri = new Uri(endpoint, relativePath);
        using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + controlToken);
        request.Headers.TryAddWithoutValidation("X-RWT-Node-Token", controlToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new NodeAdminException(
                response.StatusCode,
                $"Node admin request failed with HTTP {(int)response.StatusCode}.",
                requestMethod: "GET",
                requestUri: absoluteUri.ToString(),
                responseBody: body);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return value ?? throw new InvalidOperationException("Node admin response body was empty.");
    }
}
