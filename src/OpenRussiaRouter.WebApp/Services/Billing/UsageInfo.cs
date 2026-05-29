namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record UsageInfo(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ModelId,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    decimal CostUsd,
    string Status,
    DateTimeOffset CreatedAt);