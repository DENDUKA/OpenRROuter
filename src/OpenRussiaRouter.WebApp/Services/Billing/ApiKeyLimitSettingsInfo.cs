namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record ApiKeyLimitSettingsInfo(
    string ApiKeyId,
    decimal? DailyLimitUsd,
    decimal? WeeklyLimitUsd);

