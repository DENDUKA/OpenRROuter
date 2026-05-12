using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

/// <summary>
/// Enabled public model and its active pricing snapshot.
/// </summary>
/// <param name="ModelId">Public model id.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="RouterModelId">Internal 9Router model mapping.</param>
/// <param name="PriceVersion">Pricing version.</param>
/// <param name="Price">Per-million-token price.</param>
/// <param name="Enabled">Whether model is available.</param>
public sealed record ModelPrice(
    string ModelId,
    string DisplayName,
    string RouterModelId,
    string PriceVersion,
    ModelTokenPrice Price,
    bool Enabled);

