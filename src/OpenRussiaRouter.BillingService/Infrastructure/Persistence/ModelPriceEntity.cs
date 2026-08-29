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
    public string? DescriptionRu { get; set; }
    public string? DescriptionEn { get; set; }
    public int? ContextLength { get; set; }
    public string? Modalities { get; set; }
    public string? InputModalities { get; set; }
    public string? OutputModalities { get; set; }
    public string? DescriptionSource { get; set; }
    public string[]? TagIds { get; set; }
    public string? Provider { get; set; }
    public string? ProviderId { get; set; }
    public string ModelType { get; set; } = "text";
    public bool SupportsReasoning { get; set; }
    public string AllowedReasoningEfforts { get; set; } = string.Empty;
    public string? DefaultReasoningEffort { get; set; }
    public bool ReasoningEffortsOverridden { get; set; }
    public int Priority { get; set; }
}
