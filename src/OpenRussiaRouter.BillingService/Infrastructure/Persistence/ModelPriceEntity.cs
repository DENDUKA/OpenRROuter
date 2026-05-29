using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class ModelPriceEntity
{
    public string ModelId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string RouterModelId { get; set; } = string.Empty;

    public string PriceVersion { get; set; } = string.Empty;

    public decimal InputPerMillionAmount { get; set; }

    public decimal OutputPerMillionAmount { get; set; }

    public decimal CachePerMillionAmount { get; set; }

    public Currency Currency { get; set; }

    public bool Enabled { get; set; }

    public string? Description { get; set; }

    public int? ContextLength { get; set; }

    public string? Modalities { get; set; }

    public string? DescriptionSource { get; set; }

    public string[] TagIds { get; set; } = [];
}