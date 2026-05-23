namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record ActivitySummaryInfo(
    long TotalRequests,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCacheTokens,
    decimal TotalCostRub,
    IReadOnlyList<ActivityModelSummaryInfo> Models,
    IReadOnlyList<ActivityKeySummaryInfo> Keys);

public sealed record ActivityModelSummaryInfo(
    string ModelId,
    long Requests,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    decimal CostRub);

public sealed record ActivityKeySummaryInfo(
    string ApiKeyId,
    string ApiKeyPrefix,
    long Requests,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    decimal CostRub);
