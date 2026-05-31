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

        var id = upstreamModelId.Trim();
        var normId = Normalize(id);
        var normSlug = Normalize(Slug(id));

        // Strategy ladder, from most to least confident. First hit wins.
        var match =
            // 1. Exact id (case-insensitive).
            catalog.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
            // 2. Strip versioning/variant suffix on either side, then exact.
            ?? catalog.FirstOrDefault(e => string.Equals(StripSuffix(e.Id), StripSuffix(id), StringComparison.OrdinalIgnoreCase))
            // 3. Fully normalized id (separators/case/punctuation removed).
            ?? catalog.FirstOrDefault(e => Normalize(e.Id) == normId)
            // 4. Slug-only match — handles vendor prefix present on one side only
            //    (e.g. upstream "gpt-4o" vs OpenRouter "openai/gpt-4o").
            ?? catalog.FirstOrDefault(e => Normalize(Slug(e.Id)) == normSlug)
            // 5. Cross match: full upstream id vs candidate slug, or vice versa.
            ?? catalog.FirstOrDefault(e => Normalize(Slug(e.Id)) == normId || Normalize(e.Id) == normSlug)
            // 6. Fall back to the human-readable name (after stripping the "Vendor:" prefix).
            ?? catalog.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Name) && Normalize(NameSlug(e.Name)) == normSlug);

        if (match is null) return null;

        return new OpenRouterMatchResult(
            match.Id,
            string.IsNullOrWhiteSpace(match.Description) ? null : match.Description.Trim(),
            match.ContextLength is > 0 ? match.ContextLength : null,
            ParseModalities(match.Architecture?.Modality),
            ParseProvider(match));
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
        // OpenRouter names look like "OpenAI: GPT-4o" — the part before ':' is the vendor.
        if (!string.IsNullOrWhiteSpace(entry.Name))
        {
            var colon = entry.Name.IndexOf(':');
            if (colon > 0)
                return entry.Name[..colon].Trim();
        }

        // Fall back to the vendor prefix of the id ("openai/gpt-4o" -> "Openai").
        var slash = entry.Id.IndexOf('/');
        if (slash > 0)
        {
            var vendor = entry.Id[..slash];
            return vendor.Length > 0 ? char.ToUpperInvariant(vendor[0]) + vendor[1..] : null;
        }

        return null;
    }

    private static string? ParseModalities(string? modality)
    {
        if (string.IsNullOrWhiteSpace(modality)) return null;

        var inputPart = modality.Split("->")[0];
        var parts = inputPart.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? string.Join(", ", parts) : null;
    }
}
