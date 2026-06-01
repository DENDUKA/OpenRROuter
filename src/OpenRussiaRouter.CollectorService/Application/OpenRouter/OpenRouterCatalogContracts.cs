using System.Text.Json.Serialization;

namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed record OpenRouterModelsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<OpenRouterModelDto> Data);

public sealed record OpenRouterModelDto(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("author_display_name")] string? AuthorDisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("context_length")] int? ContextLength,
    [property: JsonPropertyName("input_modalities")] IReadOnlyList<string>? InputModalities,
    [property: JsonPropertyName("endpoint")] OpenRouterEndpointDto? Endpoint);

public sealed record OpenRouterEndpointDto(
    [property: JsonPropertyName("pricing")] OpenRouterPricingDto? Pricing);

public sealed record OpenRouterPricingDto(
    [property: JsonPropertyName("prompt")] string? Prompt,
    [property: JsonPropertyName("completion")] string? Completion,
    [property: JsonPropertyName("input_cache_read")] string? InputCacheRead);

/// <summary>Flattened catalog entry held in-memory and projected into the gRPC response.</summary>
public sealed record OpenRouterEntry(
    string Slug,
    string Name,
    string AuthorDisplayName,
    string Description,
    int ContextLength,
    IReadOnlyList<string> InputModalities,
    string PricePrompt,
    string PriceCompletion,
    string PriceInputCacheRead);
