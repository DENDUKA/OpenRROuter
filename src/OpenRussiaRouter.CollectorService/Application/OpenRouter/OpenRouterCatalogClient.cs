using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.CollectorService.Application.Collection;

namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed class OpenRouterCatalogClient(
    HttpClient httpClient,
    INodeListProvider nodeListProvider,
    ILogger<OpenRouterCatalogClient> logger)
{
    private const string NodeOpenRouterEndpointPath = "/api/integrations/openrouter/models";

    public async Task<IReadOnlyList<OpenRouterEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        // 1. First attempt: fetch via an active 9Router node (which has outbound access outside restricted regions).
        try
        {
            var nodes = await nodeListProvider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
            var routableNodes = nodes.Where(NodeCatalogCollector.IsRoutableForCatalog).ToList();

            foreach (var node in routableNodes)
            {
                try
                {
                    var nodeEntries = await TryLoadFromNodeAsync(node, cancellationToken).ConfigureAwait(false);
                    if (nodeEntries.Count > 0)
                    {
                        logger.LogInformation(
                            "Loaded {Count} OpenRouter catalog entries via node '{NodeId}' ({Endpoint})",
                            nodeEntries.Count, node.NodeId, node.EndpointUrl);
                        return nodeEntries;
                    }
                }
                catch (Exception ex) when (
                    ex is HttpRequestException or JsonException ||
                    (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    logger.LogWarning(ex, "Failed to load OpenRouter catalog from node '{NodeId}' ({Endpoint})", node.NodeId, node.EndpointUrl);
                }
            }
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to resolve node list for OpenRouter catalog polling (will try direct fetch)");
        }

        // 2. Direct fallback: query OpenRouter public endpoint directly.
        try
        {
            var response = await httpClient.GetFromJsonAsync<OpenRouterModelsResponse>(
                "https://openrouter.ai/api/v1/models", cancellationToken).ConfigureAwait(false);
            var data = response?.Data ?? [];
            var directEntries = MapEntries(data);
            if (directEntries.Count > 0)
            {
                logger.LogInformation("Loaded {Count} OpenRouter catalog entries directly from openrouter.ai", directEntries.Count);
            }
            return directEntries;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Failed to load OpenRouter model catalog directly");
            return [];
        }
    }

    private async Task<IReadOnlyList<OpenRouterEntry>> TryLoadFromNodeAsync(CollectorNode node, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(node.ControlToken))
            return [];

        var uri = new Uri(node.EndpointUrl, NodeOpenRouterEndpointPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + node.ControlToken);
        request.Headers.TryAddWithoutValidation("X-ORR-Node-Token", node.ControlToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Node '{NodeId}' returned HTTP {StatusCode} for OpenRouter catalog", node.NodeId, (int)response.StatusCode);
            return [];
        }

        var modelsResponse = await response.Content.ReadFromJsonAsync<OpenRouterModelsResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return MapEntries(modelsResponse?.Data ?? []);
    }

    private static IReadOnlyList<OpenRouterEntry> MapEntries(IReadOnlyList<OpenRouterModelDto> data) =>
        data
            .Where(d => !string.IsNullOrWhiteSpace(d.Slug))
            .Select(d => new OpenRouterEntry(
                d.Slug,
                d.Name ?? string.Empty,
                d.AuthorDisplayName ?? string.Empty,
                d.Description ?? string.Empty,
                d.ContextLength ?? 0,
                d.Architecture?.InputModalities ?? [],
                d.Pricing?.Prompt ?? string.Empty,
                d.Pricing?.Completion ?? string.Empty,
                d.Pricing?.InputCacheRead ?? string.Empty))
            .ToList();
}
