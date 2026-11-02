namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record BotModelUsageInfo(string ModelId, long TotalTokens, decimal CostUsd, long Requests);

public sealed record BotUserUsageInfo(string UserId, long TotalTokens, decimal CostUsd, IReadOnlyList<BotModelUsageInfo> ModelUsages);

public sealed record BotDailyStats(
    int TopupsCount,
    decimal TotalRub,
    decimal TotalUsd,
    int ActiveApiUsers,
    IReadOnlyList<BotUserUsageInfo> UserUsages)
{
    public void Deconstruct(out int topupsCount, out decimal totalRub, out decimal totalUsd, out int activeApiUsers)
    {
        topupsCount = TopupsCount;
        totalRub = TotalRub;
        totalUsd = TotalUsd;
        activeApiUsers = ActiveApiUsers;
    }
}
