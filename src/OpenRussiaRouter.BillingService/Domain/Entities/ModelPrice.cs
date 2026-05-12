using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record ModelPrice(
    string ModelId,
    string DisplayName,
    string RouterModelId,
    string PriceVersion,
    ModelTokenPrice Price,
    bool Enabled);

