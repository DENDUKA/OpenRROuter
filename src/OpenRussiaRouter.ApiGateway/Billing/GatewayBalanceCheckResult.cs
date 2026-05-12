using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.ApiGateway.Billing;

/// <summary>
/// Result of a Gateway balance/model check.
/// </summary>
/// <param name="Allowed">Whether request may start.</param>
/// <param name="ModelId">Public model id.</param>
/// <param name="PriceVersion">Pricing version.</param>
/// <param name="Balance">Current balance.</param>
/// <param name="ErrorCode">Machine-readable error code.</param>
public sealed record GatewayBalanceCheckResult(
    bool Allowed,
    string ModelId,
    string? PriceVersion,
    MoneyAmount Balance,
    string? ErrorCode);

