using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;
using CollectorGrpc = OpenRussiaRouter.Contracts.Grpc.Collector.V1.Collector;

namespace OpenRussiaRouter.WebApp.Services.Enrichment;

public sealed class OpenRouterCatalogClient(
    CollectorGrpc.CollectorClient collectorClient,
    IHttpClientFactory httpClientFactory,
    ILogger<OpenRouterCatalogClient> logger) : IOpenRouterCatalogClient
{
    private static readonly string[] _strippableSuffixes = [":free", ":beta", ":nitro", ":extended", ":thinking", ":online"];

    // Reasoning-effort qualifiers some providers append to the model id (e.g. "gpt-oss-120b-medium",
    // "gemini-3.1-pro-low"). OpenRouter slugs don't carry these, so we strip them — but only as a
    // fallback after exact strategies, because real slugs can also end this way (mistral-medium, o4-mini-high).
    // We also support the "-review" suffix that is common in 9Router.
    private static readonly string[] _effortQualifiers = ["minimal", "low", "medium", "high", "xhigh", "max"];

    private static readonly string[] _9routerSuffixes = ["-review", "-spark", "-search", "-thought"];

    public async Task<IReadOnlyList<OpenRouterModelEntry>> LoadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        // First try the CollectorService gRPC cache (the normal path).
        var grpcCatalog = await TryLoadFromCollectorAsync(cancellationToken);
        if (grpcCatalog.Count > 0)
            return grpcCatalog;

        // Fallback: fetch directly from OpenRouter public API. This keeps price lookup
        // working even when CollectorService is down or hasn't fetched the catalog yet.
        logger.LogWarning("OpenRouter catalog from collector is empty; falling back to direct OpenRouter API fetch");
        return await TryLoadFromOpenRouterDirectAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<OpenRouterModelEntry>> TryLoadFromCollectorAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await collectorClient.GetOpenRouterCatalogAsync(
                new GetOpenRouterCatalogRequest(),
                cancellationToken: cancellationToken);

            if (!response.Available)
                return [];

            return response.Entries.Select(e => new OpenRouterModelEntry(
                Slug: e.Slug,
                Name: string.IsNullOrEmpty(e.Name) ? null : e.Name,
                ShortName: null,
                AuthorDisplayName: string.IsNullOrEmpty(e.AuthorDisplayName) ? null : e.AuthorDisplayName,
                Description: string.IsNullOrEmpty(e.Description) ? null : e.Description,
                ContextLength: e.ContextLength > 0 ? e.ContextLength : null,
                InputModalities: e.InputModalities.ToList(),
                Pricing: new OpenRouterPricing(
                    Prompt: string.IsNullOrEmpty(e.PricePrompt) ? null : e.PricePrompt,
                    Completion: string.IsNullOrEmpty(e.PriceCompletion) ? null : e.PriceCompletion,
                    InputCacheRead: string.IsNullOrEmpty(e.PriceInputCacheRead) ? null : e.PriceInputCacheRead))).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load OpenRouter catalog from collector");
            return [];
        }
    }

    private async Task<IReadOnlyList<OpenRouterModelEntry>> TryLoadFromOpenRouterDirectAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("OpenRouterDirect");
            var response = await client.GetFromJsonAsync<OpenRouterDirectResponse>(
                "api/v1/models", cancellationToken).ConfigureAwait(false);

            var data = response?.Data ?? [];
            return data
                .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                .Select(d => new OpenRouterModelEntry(
                    Slug: d.Id!,
                    Name: string.IsNullOrEmpty(d.Name) ? null : d.Name,
                    ShortName: null,
                    AuthorDisplayName: null,
                    Description: string.IsNullOrEmpty(d.Description) ? null : d.Description,
                    ContextLength: d.ContextLength > 0 ? d.ContextLength : null,
                    InputModalities: (d.Architecture?.InputModalities ?? []).ToList(),
                    Pricing: new OpenRouterPricing(
                        Prompt: d.Pricing?.Prompt,
                        Completion: d.Pricing?.Completion,
                        InputCacheRead: d.Pricing?.InputCacheRead)))
                .ToList();
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Direct OpenRouter API fetch failed");
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

        // Same lookup with any trailing reasoning-effort qualifier removed (e.g. "...-low").
        var idEff = StripEffort(id);
        var normIdEff = Normalize(idEff);
        var normSlugEff = Normalize(Slug(idEff));

        // Defensive: the frontend feed is large and undocumented — drop entries without a slug.
        var entries = catalog.Where(e => !string.IsNullOrWhiteSpace(e.Slug)).ToList();

        // Strategy ladder, from most to least confident. First hit wins.
        var match =
        // 1. Exact slug (case-insensitive).
        entries.FirstOrDefault(e => string.Equals(e.Slug, id, StringComparison.OrdinalIgnoreCase))
        // 1b. Exact slug without vendor prefix
        ?? entries.FirstOrDefault(e => string.Equals(e.Slug, idEff, StringComparison.OrdinalIgnoreCase))
        // 2. Strip versioning/variant suffix on either side, then exact.
            ?? entries.FirstOrDefault(e => string.Equals(StripSuffix(e.Slug), StripSuffix(id), StringComparison.OrdinalIgnoreCase))
            // 3. Fully normalized slug (separators/case/punctuation removed).
            ?? entries.FirstOrDefault(e => Normalize(e.Slug) == normId)
        // 4. Slug-tail match — handles vendor prefix present on one side only
        //    (e.g. upstream "gpt-4o" vs OpenRouter "openai/gpt-4o").
        ?? entries.FirstOrDefault(e => Normalize(Slug(e.Slug)) == normSlug)
        // 4b. Match if upstream id is literally just the slug without vendor (e.g. "gemini-3.1-pro-preview")
        ?? entries.FirstOrDefault(e => string.Equals(Slug(e.Slug), id, StringComparison.OrdinalIgnoreCase))
        // 5. Cross match: full upstream id vs candidate tail, or vice versa.
            ?? entries.FirstOrDefault(e => Normalize(Slug(e.Slug)) == normId || Normalize(e.Slug) == normSlug)
            // 6. Fall back to the human-readable name (after stripping the "Vendor:" prefix).
            ?? entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Name) && Normalize(NameSlug(e.Name!)) == normSlug)
        // 7-9. Repeat the slug/cross/name strategies after stripping the effort qualifier
        //      (e.g. "ag/gpt-oss-120b-medium" -> "gpt-oss-120b" == "openai/gpt-oss-120b").
        ?? entries.FirstOrDefault(e => string.Equals(Slug(e.Slug), Slug(idEff), StringComparison.OrdinalIgnoreCase))
        ?? entries.FirstOrDefault(e => Normalize(Slug(e.Slug)) == normSlugEff)
        ?? entries.FirstOrDefault(e => Normalize(Slug(e.Slug)) == normIdEff || Normalize(e.Slug) == normSlugEff)
            ?? entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Name) && Normalize(NameSlug(e.Name!)) == normSlugEff);

        // 10. Last resort: provider appended trailing tokens OpenRouter expresses as a longer slug
        //     (e.g. "ag/gemini-3.1-pro-low" -> "google/gemini-3.1-pro-preview"). Match catalog tails
        //     that extend the effort-stripped upstream tail at a separator boundary; prefer the shortest.
        if (match is null)
        {
            var upTail = Slug(idEff).ToLowerInvariant();
            if (Normalize(upTail).Length >= 4)
                match = entries
                    .Where(e => StartsAtBoundary(Slug(e.Slug).ToLowerInvariant(), upTail))
                    .OrderBy(e => Slug(e.Slug).Length)
                    .FirstOrDefault();
        }

        if (match is null) return null;

        var pricing = match.Pricing;
        return new OpenRouterMatchResult(
            match.Slug,
            string.IsNullOrWhiteSpace(match.Description) ? null : match.Description.Trim(),
            match.ContextLength is > 0 ? match.ContextLength : null,
            FormatModalities(match.InputModalities),
            FormatModalities(match.InputModalities),
            "text",
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

    /// <summary>Removes a single trailing "-{minimal|low|medium|high}" reasoning-effort token, as well as 9Router suffixes.</summary>
    private static string StripEffort(string modelId)
    {
        // First strip any 9Router-specific suffixes
        foreach (var suffix in _9routerSuffixes)
        {
            if (modelId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                modelId = modelId[..^suffix.Length];
                // Continue checking as we might have "-high-review" which becomes "-high" and needs further stripping
            }
        }

        var dash = modelId.LastIndexOf('-');
        if (dash > 0 && dash < modelId.Length - 1)
        {
            var tail = modelId[(dash + 1)..];
            if (_effortQualifiers.Contains(tail, StringComparer.OrdinalIgnoreCase) || 
                string.Equals(tail, "none", StringComparison.OrdinalIgnoreCase))
                return modelId[..dash];
        }
        return modelId;
    }

    /// <summary>True when <paramref name="candidateTail"/> strictly extends <paramref name="prefix"/> at a separator boundary.</summary>
    private static bool StartsAtBoundary(string candidateTail, string prefix)
    {
        if (candidateTail.Length <= prefix.Length) return false;
        if (!candidateTail.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var next = candidateTail[prefix.Length];
        return next is '-' or '.' or '_' or ' ';
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
