using System.Net.Http.Json;
using System.Text.Json;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Http;

public sealed class RouterNodeProvider(HttpClient httpClient)
    : IRouterNodeProvider
{
    private const string ProviderQuotaPath = "/api/integrations/providers/quota";
    private const string ProviderStatusPathTemplate = "/api/integrations/providers/{0}/status";
    private const string ProviderModelsPathTemplate = "/api/integrations/providers/by-type/{0}/models";
    private const string IntegrationKeysPath = "/api/integrations/keys";
    private const string IntegrationKeyStatusPathTemplate = "/api/integrations/keys/{0}/status";
    private const string ModelsPath = "/v1/models";
    private const string ProvidersPath = "/api/integrations/providers";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string DescribeListProvidersRequest(RouterNode node) =>
        DescribeRequest(node, HttpMethod.Get, ProvidersPath);

    public string DescribeListModelsRequest(RouterNode node) =>
        DescribeRequest(node, HttpMethod.Get, ModelsPath);

    public Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
        RouterNode node,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var path = includeInactive
            ? $"{ProviderQuotaPath}?includeInactive=true"
            : ProviderQuotaPath;

        return SendAsync<RouterProviderQuotaResponse>(node, HttpMethod.Get, path, content: null, cancellationToken);
    }

    public Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
        RouterNode node,
        string connectionId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        return SendAsync<RouterProviderStatusResponse>(
            node,
            HttpMethod.Patch,
            string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                ProviderStatusPathTemplate,
                Uri.EscapeDataString(connectionId)),
            JsonContent.Create(new ProviderStatusRequest(isActive), options: JsonOptions),
            cancellationToken);
    }

    public Task<RouterProviderModelsResponse> ListProviderModelsAsync(
        RouterNode node,
        string provider,
        string? source,
        bool includeCompatible,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(source))
            query.Add($"source={Uri.EscapeDataString(source.Trim())}");
        if (includeCompatible)
            query.Add("includeCompatible=true");

        var path = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            ProviderModelsPathTemplate,
            Uri.EscapeDataString(provider.Trim()));
        if (query.Count > 0)
            path += "?" + string.Join("&", query);

        return SendAsync<RouterProviderModelsResponse>(node, HttpMethod.Get, path, content: null, cancellationToken);
    }

    public Task<RouterIntegrationKeysResponse> ListIntegrationKeysAsync(
        RouterNode node,
        CancellationToken cancellationToken)
    {
        return SendAsync<RouterIntegrationKeysResponse>(node, HttpMethod.Get, IntegrationKeysPath, content: null, cancellationToken);
    }

    public Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
        RouterNode node,
        string name,
        string key,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return SendAsync<RouterIntegrationKeyCreateResponse>(
            node,
            HttpMethod.Post,
            IntegrationKeysPath,
            JsonContent.Create(new CreateIntegrationKeyRequest(name, key), options: JsonOptions),
            cancellationToken);
    }

    public Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
        RouterNode node,
        string keyId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        return SendAsync<RouterIntegrationKeyInfo>(
            node,
            HttpMethod.Patch,
            string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                IntegrationKeyStatusPathTemplate,
                Uri.EscapeDataString(keyId)),
            JsonContent.Create(new ProviderStatusRequest(isActive), options: JsonOptions),
            cancellationToken);
    }

    public Task<NodeModelsResponse> ListModelsAsync(
        RouterNode node,
        CancellationToken cancellationToken)
    {
        return SendAsync<NodeModelsResponse>(node, HttpMethod.Get, ModelsPath, content: null, cancellationToken);
    }

    public Task<RouterProviderListResponse> ListProvidersAsync(
        RouterNode node,
        CancellationToken cancellationToken)
    {
        return SendAsync<RouterProviderListResponse>(
            node,
            HttpMethod.Get,
            ProvidersPath,
            content: null,
            cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(
        RouterNode node,
        HttpMethod method,
        string relativePathAndQuery,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (string.IsNullOrWhiteSpace(node.ControlToken))
        {
            throw new InvalidOperationException($"Control token is not set for node '{node.Id}'.");
        }

        var absoluteUri = new Uri(node.EndpointUrl, relativePathAndQuery);
        using var request = new HttpRequestMessage(method, absoluteUri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + node.ControlToken);
        request.Headers.TryAddWithoutValidation("X-ORR-Node-Token", node.ControlToken);
        request.Content = content;

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderExceptionAsync(response, method, absoluteUri, cancellationToken).ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

        return value ?? throw new InvalidOperationException("Router provider response body was empty.");
    }

    private static async Task ThrowProviderExceptionAsync(
        HttpResponseMessage response,
        HttpMethod method,
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var routerError = TryReadError(body);
        var message = routerError is null
            ? $"Router provider request failed with HTTP {(int)response.StatusCode}."
            : $"Router provider request failed with HTTP {(int)response.StatusCode}: {routerError}";

        throw new RouterNodeProviderException(
            response.StatusCode,
            routerError,
            message,
            requestMethod: method.Method,
            requestUri: requestUri.ToString(),
            responseBody: body);
    }

    private static string? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string DescribeRequest(RouterNode node, HttpMethod method, string relativePathAndQuery)
    {
        ArgumentNullException.ThrowIfNull(node);
        return $"{method.Method} {new Uri(node.EndpointUrl, relativePathAndQuery)}";
    }

    private sealed record ProviderStatusRequest(bool IsActive);
    private sealed record CreateIntegrationKeyRequest(string Name, string Key);
}
