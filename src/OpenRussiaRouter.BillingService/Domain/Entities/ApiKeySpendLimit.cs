using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record ApiKeySpendLimit(
    string UserId,
    string ApiKeyId,
    MoneyAmount? DailyLimit,
    MoneyAmount? WeeklyLimit,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

