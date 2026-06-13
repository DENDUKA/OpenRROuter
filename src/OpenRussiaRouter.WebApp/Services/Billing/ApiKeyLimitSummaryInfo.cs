namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record ApiKeyLimitSummaryInfo(
    string ApiKeyId,
    decimal? DailyLimitUsd,
    decimal? WeeklyLimitUsd,
    decimal DailySpentUsd,
    decimal WeeklySpentUsd,
    decimal DailyRemainingUsd,
    decimal WeeklyRemainingUsd,
    bool DailyExceeded,
    bool WeeklyExceeded);

