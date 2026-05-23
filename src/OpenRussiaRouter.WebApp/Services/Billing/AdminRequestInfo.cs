namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record AdminRequestInfo(
    string RequestId,
    string UserId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    string? ProviderName,
    string? ProviderAccount,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    decimal CostRub,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset CompletedAt);
