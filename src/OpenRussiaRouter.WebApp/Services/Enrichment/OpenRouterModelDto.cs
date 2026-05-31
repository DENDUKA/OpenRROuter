using System.Text.Json.Serialization;

namespace OpenRussiaRouter.WebApp.Services.Enrichment;

// NOTE: we read OpenRouter's *frontend* catalog (`/api/frontend/models`), not the public
// `/api/v1/models`. The public endpoint truncates descriptions to a ~240-char preview ending
// in "..." and carries no pricing. The frontend feed has full descriptions, modalities,
// provider display names and per-token pricing.

public sealed record OpenRouterModelsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<OpenRouterModelEntry> Data);

public sealed record OpenRouterModelEntry(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("short_name")] string? ShortName,
    [property: JsonPropertyName("author_display_name")] string? AuthorDisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("context_length")] int? ContextLength,
    [property: JsonPropertyName("input_modalities")] IReadOnlyList<string>? InputModalities,
    [property: JsonPropertyName("endpoint")] OpenRouterEndpoint? Endpoint);

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
    string? Provider,
    decimal? InputPrice,
    decimal? OutputPrice,
    decimal? CachePrice);
