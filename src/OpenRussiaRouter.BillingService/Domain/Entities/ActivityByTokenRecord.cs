using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record ActivityByTokenRecord(
    string RequestId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    TokenUsage Usage,
    MoneyAmount Cost,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? ProviderName = null,
    string? ProviderAccount = null);
