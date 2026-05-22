namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record ActivityByTokenInfo(
    string RequestId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    decimal CostRub,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset CompletedAt);
