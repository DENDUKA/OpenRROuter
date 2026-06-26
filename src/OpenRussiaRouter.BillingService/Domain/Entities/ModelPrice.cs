using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record ModelPrice(
    string ModelId,
    string DisplayName,
    string RouterModelId,
    string PriceVersion,
    ModelTokenPrice Price,
    bool Enabled,
    string? Description = null,
    int? ContextLength = null,
    string? Modalities = null,
    string? InputModalities = null,
    string? OutputModalities = null,
    string? DescriptionSource = null,
    IReadOnlyList<string>? TagIds = null,
    string? Provider = null,
    string ModelType = "text",
    bool SupportsReasoning = false,
    IReadOnlyList<string>? AllowedReasoningEfforts = null,
    string? DefaultReasoningEffort = null,
    int Priority = 0);

