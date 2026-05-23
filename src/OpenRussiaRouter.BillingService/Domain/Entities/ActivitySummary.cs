using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record ActivitySummary(
    long TotalRequests,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCacheTokens,
    MoneyAmount TotalCost,
    IReadOnlyList<ActivityModelSummary> Models,
    IReadOnlyList<ActivityKeySummary> Keys);

public sealed record ActivityModelSummary(
    string ModelId,
    long Requests,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    MoneyAmount Cost);

public sealed record ActivityKeySummary(
    string ApiKeyId,
    string ApiKeyPrefix,
    long Requests,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    MoneyAmount Cost);
