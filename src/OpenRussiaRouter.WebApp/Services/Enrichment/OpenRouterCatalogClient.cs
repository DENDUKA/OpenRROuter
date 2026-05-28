using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace OpenRussiaRouter.WebApp.Services.Enrichment;

public sealed class OpenRouterCatalogClient(
    HttpClient httpClient,
    ILogger<OpenRouterCatalogClient> logger) : IOpenRouterCatalogClient
{
    private static readonly string[] _strippableSuffixes = [":free", ":beta", ":nitro", ":extended"];

    public async Task<IReadOnlyList<OpenRouterModelEntry>> LoadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<OpenRouterModelsResponse>(
                "api/v1/models", cancellationToken);
            return response?.Data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load OpenRouter model catalog");
            return [];
        }
    }

    public OpenRouterMatchResult? FindMatch(
        IReadOnlyList<OpenRouterModelEntry> catalog,
        string upstreamModelId)
    {
        if (string.IsNullOrWhiteSpace(upstreamModelId) || catalog.Count == 0)
            return null;

        var match = catalog.FirstOrDefault(e =>
            string.Equals(e.Id, upstreamModelId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var stripped = StripSuffix(upstreamModelId);
            if (stripped != upstreamModelId)
                match = catalog.FirstOrDefault(e =>
                    string.Equals(e.Id, stripped, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(StripSuffix(e.Id), stripped, StringComparison.OrdinalIgnoreCase));
        }

        if (match is null) return null;

        return new OpenRouterMatchResult(
            match.Id,
            string.IsNullOrWhiteSpace(match.Description) ? null : match.Description.Trim(),
            match.ContextLength is > 0 ? match.ContextLength : null,
            ParseModalities(match.Architecture?.Modality));
    }

    private static string StripSuffix(string modelId)
    {
        foreach (var suffix in _strippableSuffixes)
            if (modelId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return modelId[..^suffix.Length];
        return modelId;
    }

    private static string? ParseModalities(string? modality)
    {
        if (string.IsNullOrWhiteSpace(modality)) return null;

        var inputPart = modality.Split("->")[0];
        var parts = inputPart.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? string.Join(", ", parts) : null;
    }
}