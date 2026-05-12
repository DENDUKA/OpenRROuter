using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Billing;

/// <summary>
/// Usage event sent from Gateway to Billing after a router response.
/// </summary>
/// <param name="RequestId">Gateway request id.</param>
/// <param name="UserId">User id.</param>
/// <param name="ApiKeyId">API key id.</param>
/// <param name="ModelId">Public model id.</param>
/// <param name="RouterNodeId">Router node id.</param>
/// <param name="Usage">Token usage.</param>
/// <param name="Status">Request status.</param>
/// <param name="StartedAtUtc">Start timestamp.</param>
/// <param name="CompletedAtUtc">Completion timestamp.</param>
public sealed record GatewayUsageEvent(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ModelId,
    string RouterNodeId,
    TokenUsage Usage,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

