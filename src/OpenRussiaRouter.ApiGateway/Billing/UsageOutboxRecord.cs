using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed record UsageOutboxRecord(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    TokenUsage Usage,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? ProviderName,
    string? ProviderAccount,
    string? Endpoint,
    bool Stream,
    int Attempts,
    DateTimeOffset NextAttemptAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LeasedUntilUtc)
{
    public GatewayUsageEvent ToGatewayUsageEvent()
    {
        return new GatewayUsageEvent(
            RequestId,
            UserId,
            ApiKeyId,
            ApiKeyPrefix,
            ModelId,
            RouterNodeId,
            Usage,
            Status,
            StartedAtUtc,
            CompletedAtUtc,
            ProviderName,
            ProviderAccount,
            Endpoint,
            Stream);
    }
}
