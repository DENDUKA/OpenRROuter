using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.Contracts.Billing;

/// <summary>
/// Result of checking whether a request can start for a model and account balance.
/// </summary>
/// <param name="Allowed">Whether the request can start.</param>
/// <param name="ModelId">Public model id.</param>
/// <param name="PriceVersion">Pricing snapshot/version.</param>
/// <param name="CurrentBalance">Current account balance.</param>
/// <param name="ErrorCode">Machine-readable error when denied.</param>
public sealed record BalanceCheckResult(
    bool Allowed,
    string ModelId,
    string? PriceVersion,
    MoneyAmount CurrentBalance,
    string? ErrorCode);

/// <summary>
/// Public model entry returned by the OpenAI-compatible model list.
/// </summary>
/// <param name="Id">Public model id.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="Enabled">Whether the model is available.</param>
public sealed record PublicModel(string Id, string DisplayName, bool Enabled);

/// <summary>
/// Usage event sent by Gateway after a completed 9Router request.
/// </summary>
/// <param name="RequestId">Stable request id for idempotency.</param>
/// <param name="UserId">User id.</param>
/// <param name="ApiKeyId">API key id.</param>
/// <param name="ModelId">Public model id.</param>
/// <param name="RouterNodeId">Router node id used for the request.</param>
/// <param name="Usage">Token usage reported by 9Router.</param>
/// <param name="Status">Final request status.</param>
/// <param name="StartedAtUtc">Request start timestamp.</param>
/// <param name="CompletedAtUtc">Request completion timestamp.</param>
public sealed record UsageEvent(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ModelId,
    string RouterNodeId,
    TokenUsage Usage,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

