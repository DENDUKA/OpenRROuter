using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record AdminRequestRecord(
    string RequestId,
    string UserId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    string? ProviderName,
    string? ProviderAccount,
    TokenUsage Usage,
    MoneyAmount Cost,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);
