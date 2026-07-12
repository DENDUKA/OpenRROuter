using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.Contracts.Billing;

public sealed record BalanceCheckResult(
    bool Allowed,
    string ModelId,
    string RouterModelId,
    string? PriceVersion,
    MoneyAmount CurrentBalance,
    string? ErrorCode,
    bool SupportsReasoning = false,
    IReadOnlyList<string>? AllowedReasoningEfforts = null,
    string? DefaultReasoningEffort = null);

public sealed record PublicModel(
    string Id,
    string DisplayName,
    bool Enabled,
    bool SupportsReasoning = false,
    IReadOnlyList<string>? AllowedReasoningEfforts = null,
    string? DefaultReasoningEffort = null);

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
    DateTimeOffset CompletedAtUtc,
    string? ProviderName = null,
    string? ProviderAccount = null,
    string? Endpoint = null,
    bool? Stream = null);

public sealed record GrantStartBalanceResult(bool Granted, string? LedgerEntryId, string? ErrorCode);

public sealed record PromotionSettings(
    bool StartBalanceEnabled,
    decimal StartBalanceAmountUsd,
    bool FirstTopUpEnabled,
    decimal FirstTopUpPercent);

public sealed record GrantFirstTopUpBonusResult(bool Granted, string? LedgerEntryId, string? ErrorCode);

