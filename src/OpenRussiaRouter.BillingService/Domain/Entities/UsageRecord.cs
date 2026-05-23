using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record UsageRecord(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ModelId,
    string PriceVersion,
    string RouterNodeId,
    TokenUsage Usage,
    MoneyAmount Cost,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string? ProviderName = null,
    string? ProviderAccount = null);
