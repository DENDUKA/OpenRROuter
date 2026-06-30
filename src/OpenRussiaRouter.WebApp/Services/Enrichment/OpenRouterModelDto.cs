using System.Text.Json.Serialization;

namespace OpenRussiaRouter.WebApp.Services.Enrichment;

// NOTE: we read OpenRouter's public catalog (`/api/v1/models`). The frontend feed is deprecated/removed.
// The public endpoint has descriptions, modalities, provider display names and per-token pricing.

public sealed record OpenRouterModelsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<OpenRouterModelEntry> Data);

public sealed record OpenRouterModelEntry(
    [property: JsonPropertyName("id")] string Slug,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("short_name")] string? ShortName,
    [property: JsonPropertyName("author_display_name")] string? AuthorDisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("context_length")] int? ContextLength,
    [property: JsonPropertyName("input_modalities")] IReadOnlyList<string>? InputModalities,
    [property: JsonPropertyName("pricing")] OpenRouterPricing? Pricing);

public sealed record OpenRouterEndpoint(
    [property: JsonPropertyName("pricing")] OpenRouterPricing? Pricing);

/// <summary>Per-token prices as strings (USD per single token), e.g. "0.00000098".</summary>
public sealed record OpenRouterPricing(
    [property: JsonPropertyName("prompt")] string? Prompt,
    [property: JsonPropertyName("completion")] string? Completion,
    [property: JsonPropertyName("input_cache_read")] string? InputCacheRead);

/// <summary>Результат поиска модели в каталоге OpenRouter. Цены — в $ за 1M токенов.</summary>
public sealed record OpenRouterMatchResult(
    string MatchedId,
    string? Description,
    int? ContextLength,
    string? Modalities,
    string? InputModalities,
    string? OutputModalities,
    string? Provider,
    decimal? InputPrice,
    decimal? OutputPrice,
    decimal? CachePrice);
