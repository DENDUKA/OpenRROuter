using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed record GatewayBalanceCheckResult(
    bool Allowed,
    string ModelId,
    string? PriceVersion,
    MoneyAmount Balance,
    string? ErrorCode);

