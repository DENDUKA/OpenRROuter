using System.Text.Json.Serialization;

namespace OpenRussiaRouter.WebApp.Services.Enrichment;

public sealed record OpenRouterModelsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<OpenRouterModelEntry> Data);

public sealed record OpenRouterModelEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("context_length")] int? ContextLength,
    [property: JsonPropertyName("architecture")] OpenRouterArchitecture? Architecture);

public sealed record OpenRouterArchitecture(
    [property: JsonPropertyName("modality")] string? Modality);

/// <summary>Результат поиска модели в каталоге OpenRouter.</summary>
public sealed record OpenRouterMatchResult(
    string MatchedId,
    string? Description,
    int? ContextLength,
    string? Modalities,
    string? Provider);