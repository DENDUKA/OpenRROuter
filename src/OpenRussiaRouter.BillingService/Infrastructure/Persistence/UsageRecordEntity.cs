using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class UsageRecordEntity
{
    public string RequestId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string ApiKeyId { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public string PriceVersion { get; set; } = string.Empty;

    public string RouterNodeId { get; set; } = string.Empty;

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CacheTokens { get; set; }

    public decimal CostAmount { get; set; }

    public Currency CostCurrency { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}