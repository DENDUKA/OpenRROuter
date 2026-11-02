using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Api;

public static class BotDailySummaryEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/internal/bot/daily-summary", HandleAsync)
            .AllowAnonymous();

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        AuthProvider auth,
        BillingProvider billing,
        IConfiguration config,
        IOptions<SupportBotSettings> botSettings,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("BotDailySummaryEndpoint");
        var settings = botSettings.Value;

        if (string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            return Results.Json(new { error = "service_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!IsAuthorized(http, settings.AuthToken))
        {
            return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var sinceUtc = DateTimeOffset.UtcNow.AddHours(-24);

        int newRegistrations = 0;
        IReadOnlyList<UserInfo> users = Array.Empty<UserInfo>();
        try
        {
            users = await auth.ListUsersAsync(ct);
            newRegistrations = users.Count(u => u.CreatedAt >= sinceUtc);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch user registrations for daily summary.");
        }

        var adminUserIds = users
            .Where(u => string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Id)
            .ToHashSet(StringComparer.Ordinal);

        var newUsers = users
            .Where(u => u.CreatedAt >= sinceUtc && !adminUserIds.Contains(u.Id))
            .ToList();
        newRegistrations = newUsers.Count;
        var newUserIds = newUsers.Select(u => u.Id).ToList();

        var userEmails = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var u in users)
        {
            userEmails[u.Id] = u.Email;
        }

        int topupsCount = 0;
        decimal totalRub = 0m;
        decimal totalUsd = 0m;
        int activeApiUsers = 0;
        var clientsUsage = new List<object>();
        var registeredUsers = new List<object>();

        try
        {
            var stats = await billing.GetDailyStatsAsync(sinceUtc, adminUserIds, newUserIds, ct);
            topupsCount = stats.TopupsCount;
            totalRub = stats.TotalRub;
            totalUsd = stats.TotalUsd;
            activeApiUsers = stats.ActiveApiUsers;
            clientsUsage = BuildClientsUsage(stats, userEmails, adminUserIds);
            registeredUsers = BuildRegisteredUsers(newUsers, stats);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch billing topups for daily summary.");
        }

        int uniqueUsers = await GetUniqueVisitorsCountAsync(config["ProductAnalytics:ConnectionString"], sinceUtc, logger, ct);

        return Results.Ok(new
        {
            periodHours = 24,
            newRegistrations,
            topupsCount,
            topupsTotalRub = totalRub,
            topupsTotalUsd = totalUsd,
            uniqueUsers,
            activeApiUsers,
            clientsUsage,
            registeredUsers
        });
    }

    private static bool IsAuthorized(HttpContext http, string expectedToken)
    {
        var header = http.Request.Headers.Authorization.ToString();
        var expectedBearer = $"Bearer {expectedToken}";
        var altToken = http.Request.Headers["X-Bot-Token"].ToString();

        return (header.Length > 0 && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(header), Encoding.UTF8.GetBytes(expectedBearer)))
               || (altToken.Length > 0 && CryptographicOperations.FixedTimeEquals(
                   Encoding.UTF8.GetBytes(altToken), Encoding.UTF8.GetBytes(expectedToken)));
    }

    private static List<object> BuildClientsUsage(
        BotDailyStats stats,
        IReadOnlyDictionary<string, string> userEmails,
        HashSet<string> adminUserIds)
    {
        var result = new List<object>();
        foreach (var user in stats.UserUsages)
        {
            if (adminUserIds.Contains(user.UserId))
            {
                continue;
            }

            var email = userEmails.TryGetValue(user.UserId, out var foundEmail) && !string.IsNullOrWhiteSpace(foundEmail)
                ? foundEmail
                : user.UserId;

            result.Add(new
            {
                email,
                totalTokens = user.TotalTokens,
                totalCostUsd = user.CostUsd,
                hasPaidTopup = user.HasPaidTopup,
                balanceUsd = user.BalanceUsd,
                models = user.ModelUsages.Select(m => new
                {
                    modelId = m.ModelId,
                    tokens = m.TotalTokens,
                    costUsd = m.CostUsd
                }).ToList()
            });
        }
        return result;
    }

    private static List<object> BuildRegisteredUsers(
        IReadOnlyList<UserInfo> newUsers,
        BotDailyStats stats)
    {
        var newUsersDict = stats.NewUsers?.ToDictionary(nu => nu.UserId, StringComparer.Ordinal);
        var result = new List<object>();
        foreach (var u in newUsers)
        {
            var balanceUsd = 0m;
            var hasPaidTopup = false;
            if (newUsersDict != null && newUsersDict.TryGetValue(u.Id, out var stat))
            {
                balanceUsd = stat.BalanceUsd;
                hasPaidTopup = stat.HasPaidTopup;
            }

            result.Add(new
            {
                email = u.Email,
                balanceUsd,
                hasPaidTopup
            });
        }
        return result;
    }

    private static async Task<int> GetUniqueVisitorsCountAsync(
        string? analyticsCs,
        DateTimeOffset sinceUtc,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(analyticsCs)) return 0;
        try
        {
            await using var conn = new NpgsqlConnection(analyticsCs);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("select count(distinct distinct_id) from analytics.events where occurred_at_utc >= @since;", conn);
            cmd.Parameters.AddWithValue("since", sinceUtc);
            var scalar = await cmd.ExecuteScalarAsync(ct);
            return scalar is not null && scalar != DBNull.Value ? Convert.ToInt32(scalar) : 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to count unique visitors from analytics.events for daily summary.");
            return 0;
        }
    }
}
