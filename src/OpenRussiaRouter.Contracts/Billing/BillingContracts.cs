using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.Contracts.Billing;

public sealed record BalanceCheckResult(
    bool Allowed,
    string ModelId,
    string? PriceVersion,
    MoneyAmount CurrentBalance,
    string? ErrorCode);

public sealed record PublicModel(string Id, string DisplayName, bool Enabled);

public sealed record UsageEvent(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    TokenUsage Usage,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

