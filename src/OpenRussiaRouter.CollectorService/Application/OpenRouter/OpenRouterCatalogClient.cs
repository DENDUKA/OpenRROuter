using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed class OpenRouterCatalogClient(HttpClient httpClient, ILogger<OpenRouterCatalogClient> logger)
{
    public async Task<IReadOnlyList<OpenRouterEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<OpenRouterModelsResponse>(
                "api/frontend/models", cancellationToken).ConfigureAwait(false);
            var data = response?.Data ?? [];
            return data
                .Where(d => !string.IsNullOrWhiteSpace(d.Slug))
                .Select(d => new OpenRouterEntry(
                    d.Slug,
                    d.Name ?? string.Empty,
                    d.AuthorDisplayName ?? string.Empty,
                    d.Description ?? string.Empty,
                    d.ContextLength ?? 0,
                    d.InputModalities ?? [],
                    d.Endpoint?.Pricing?.Prompt ?? string.Empty,
                    d.Endpoint?.Pricing?.Completion ?? string.Empty,
                    d.Endpoint?.Pricing?.InputCacheRead ?? string.Empty))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load OpenRouter model catalog");
            return [];
        }
    }
}
