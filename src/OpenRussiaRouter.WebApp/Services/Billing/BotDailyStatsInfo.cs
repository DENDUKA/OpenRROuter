namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record BotModelUsageInfo(string ModelId, long TotalTokens, decimal CostUsd, long Requests);

public sealed record BotUserUsageInfo(string UserId, long TotalTokens, decimal CostUsd, IReadOnlyList<BotModelUsageInfo> ModelUsages, bool HasPaidTopup = false, decimal BalanceUsd = 0m);

public sealed record BotNewUserInfo(string UserId, decimal BalanceUsd, bool HasPaidTopup);

public sealed record BotDailyStats(
    int TopupsCount,
    decimal TotalRub,
    decimal TotalUsd,
    int ActiveApiUsers,
    IReadOnlyList<BotUserUsageInfo> UserUsages,
    IReadOnlyList<BotNewUserInfo>? NewUsers = null)
{
    public void Deconstruct(out int topupsCount, out decimal totalRub, out decimal totalUsd, out int activeApiUsers)
    {
        topupsCount = TopupsCount;
        totalRub = TotalRub;
        totalUsd = TotalUsd;
        activeApiUsers = ActiveApiUsers;
    }
}
