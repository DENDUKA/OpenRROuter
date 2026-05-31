using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace OpenRussiaRouter.WebApp.Services.Enrichment;

public sealed class OpenRouterCatalogClient(
    HttpClient httpClient,
    ILogger<OpenRouterCatalogClient> logger) : IOpenRouterCatalogClient
{
    private static readonly string[] _strippableSuffixes = [":free", ":beta", ":nitro", ":extended", ":thinking", ":online"];

    public async Task<IReadOnlyList<OpenRouterModelEntry>> LoadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Frontend feed — full descriptions + pricing (see OpenRouterModelDto.cs).
            var response = await httpClient.GetFromJsonAsync<OpenRouterModelsResponse>(
                "api/frontend/models", cancellationToken);
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

        var id = upstreamModelId.Trim();
        var normId = Normalize(id);
        var normSlug = Normalize(Slug(id));

        // Defensive: the frontend feed is large and undocumented — drop entries without a slug.
        var entries = catalog.Where(e => !string.IsNullOrWhiteSpace(e.Slug)).ToList();

        // Strategy ladder, from most to least confident. First hit wins.
        var match =
            // 1. Exact slug (case-insensitive).
            entries.FirstOrDefault(e => string.Equals(e.Slug, id, StringComparison.OrdinalIgnoreCase))
            // 2. Strip versioning/variant suffix on either side, then exact.
            ?? entries.FirstOrDefault(e => string.Equals(StripSuffix(e.Slug), StripSuffix(id), StringComparison.OrdinalIgnoreCase))
            // 3. Fully normalized slug (separators/case/punctuation removed).
            ?? entries.FirstOrDefault(e => Normalize(e.Slug) == normId)
            // 4. Slug-tail match — handles vendor prefix present on one side only
            //    (e.g. upstream "gpt-4o" vs OpenRouter "openai/gpt-4o").
            ?? entries.FirstOrDefault(e => Normalize(Slug(e.Slug)) == normSlug)
            // 5. Cross match: full upstream id vs candidate tail, or vice versa.
            ?? entries.FirstOrDefault(e => Normalize(Slug(e.Slug)) == normId || Normalize(e.Slug) == normSlug)
            // 6. Fall back to the human-readable name (after stripping the "Vendor:" prefix).
            ?? entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Name) && Normalize(NameSlug(e.Name!)) == normSlug);

        if (match is null) return null;

        var pricing = match.Endpoint?.Pricing;
        return new OpenRouterMatchResult(
            match.Slug,
            string.IsNullOrWhiteSpace(match.Description) ? null : match.Description.Trim(),
            match.ContextLength is > 0 ? match.ContextLength : null,
            FormatModalities(match.InputModalities),
            ParseProvider(match),
            PerMillion(pricing?.Prompt),
            PerMillion(pricing?.Completion),
            PerMillion(pricing?.InputCacheRead));
    }

    private static string StripSuffix(string modelId)
    {
        foreach (var suffix in _strippableSuffixes)
            if (modelId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return modelId[..^suffix.Length];
        return modelId;
    }

    /// <summary>Lowercased, with every non-alphanumeric character removed.</summary>
    private static string Normalize(string value)
    {
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        var len = 0;
        foreach (var ch in value)
            if (char.IsLetterOrDigit(ch))
                buffer[len++] = char.ToLowerInvariant(ch);
        return new string(buffer[..len]);
    }

    /// <summary>The part after the last '/', i.e. the model slug without its vendor prefix.</summary>
    private static string Slug(string modelId)
    {
        var slash = modelId.LastIndexOf('/');
        return slash >= 0 && slash < modelId.Length - 1 ? modelId[(slash + 1)..] : modelId;
    }

    /// <summary>The part after the "Vendor:" prefix in an OpenRouter display name.</summary>
    private static string NameSlug(string name)
    {
        var colon = name.IndexOf(':');
        return colon >= 0 && colon < name.Length - 1 ? name[(colon + 1)..].Trim() : name;
    }

    private static string? ParseProvider(OpenRouterModelEntry entry)
    {
        // The frontend feed gives a clean vendor display name directly.
        if (!string.IsNullOrWhiteSpace(entry.AuthorDisplayName))
            return entry.AuthorDisplayName.Trim();

        // Names look like "OpenAI: GPT-4o" — the part before ':' is the vendor.
        if (!string.IsNullOrWhiteSpace(entry.Name))
        {
            var colon = entry.Name.IndexOf(':');
            if (colon > 0)
                return entry.Name[..colon].Trim();
        }

        // Fall back to the vendor prefix of the slug ("openai/gpt-4o" -> "Openai").
        var slash = entry.Slug.IndexOf('/');
        if (slash > 0)
        {
            var vendor = entry.Slug[..slash];
            return vendor.Length > 0 ? char.ToUpperInvariant(vendor[0]) + vendor[1..] : null;
        }

        return null;
    }

    private static string? FormatModalities(IReadOnlyList<string>? modalities)
    {
        if (modalities is null || modalities.Count == 0) return null;
        var parts = modalities
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length > 0 ? string.Join(", ", parts) : null;
    }

    /// <summary>Converts an OpenRouter per-token price string to $ per 1M tokens.</summary>
    private static decimal? PerMillion(string? perTokenPrice)
    {
        if (string.IsNullOrWhiteSpace(perTokenPrice)) return null;
        if (!decimal.TryParse(perTokenPrice, NumberStyles.Float, CultureInfo.InvariantCulture, out var perToken))
            return null;
        return perToken < 0 ? null : decimal.Round(perToken * 1_000_000m, 6);
    }
}
