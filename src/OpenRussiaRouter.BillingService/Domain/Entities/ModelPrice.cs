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
    string? DescriptionSource = null,
    IReadOnlyList<string>? TagIds = null);

