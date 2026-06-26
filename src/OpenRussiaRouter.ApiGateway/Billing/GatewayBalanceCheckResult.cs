using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed record GatewayBalanceCheckResult(
    bool Allowed,
    string ModelId,
    string RouterModelId,
    string? PriceVersion,
    MoneyAmount Balance,
    string? ErrorCode,
    bool SupportsReasoning = false,
    IReadOnlyList<string>? AllowedReasoningEfforts = null,
    string? DefaultReasoningEffort = null);

