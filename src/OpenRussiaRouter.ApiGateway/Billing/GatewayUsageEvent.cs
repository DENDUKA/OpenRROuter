using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed record GatewayUsageEvent(
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
    string? ProviderName = null,
    string? ProviderAccount = null,
    string? Endpoint = null,
    bool? Stream = null);
