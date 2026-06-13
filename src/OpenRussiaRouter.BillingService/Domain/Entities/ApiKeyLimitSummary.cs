using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record ApiKeyLimitSummary(
    string ApiKeyId,
    MoneyAmount? DailyLimit,
    MoneyAmount? WeeklyLimit,
    MoneyAmount DailySpent,
    MoneyAmount WeeklySpent,
    MoneyAmount DailyRemaining,
    MoneyAmount WeeklyRemaining,
    bool DailyExceeded,
    bool WeeklyExceeded);

