using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class ModelPriceEntity
{
    public required string ModelId { get; set; }
    public required string DisplayName { get; set; }
    public required string RouterModelId { get; set; }
    public required string PriceVersion { get; set; }
    public decimal InputPerMillionAmount { get; set; }
    public decimal OutputPerMillionAmount { get; set; }
    public decimal CachePerMillionAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Enabled { get; set; }
    public string? Description { get; set; }
    public int? ContextLength { get; set; }
    public string? Modalities { get; set; }
    public string? DescriptionSource { get; set; }
    public string[]? TagIds { get; set; }
    public string? Provider { get; set; }
}