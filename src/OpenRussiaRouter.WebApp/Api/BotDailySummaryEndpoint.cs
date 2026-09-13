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

        var header = http.Request.Headers.Authorization.ToString();
        var expectedBearer = $"Bearer {settings.AuthToken}";
        var altToken = http.Request.Headers["X-Bot-Token"].ToString();

        bool isAuthorized = (header.Length > 0 && CryptographicOperations.FixedTimeEquals(
                                Encoding.UTF8.GetBytes(header), Encoding.UTF8.GetBytes(expectedBearer)))
                            || (altToken.Length > 0 && CryptographicOperations.FixedTimeEquals(
                                Encoding.UTF8.GetBytes(altToken), Encoding.UTF8.GetBytes(settings.AuthToken)));

        if (!isAuthorized)
        {
            return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var sinceUtc = DateTimeOffset.UtcNow.AddHours(-24);

        int newRegistrations = 0;
        try
        {
            var users = await auth.ListUsersAsync(ct);
            newRegistrations = users.Count(u => u.CreatedAt >= sinceUtc);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch user registrations for daily summary.");
        }

        int topupsCount = 0;
        decimal totalRub = 0m;
        decimal totalUsd = 0m;
        int activeApiUsers = 0;
        try
        {
            (topupsCount, totalRub, totalUsd, activeApiUsers) = await billing.GetDailyStatsAsync(sinceUtc, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch billing topups for daily summary.");
        }

        int uniqueUsers = 0;
        var analyticsCs = config["ProductAnalytics:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(analyticsCs))
        {
            try
            {
                await using var conn = new NpgsqlConnection(analyticsCs);
                await conn.OpenAsync(ct);
                await using var cmd = new NpgsqlCommand("select count(distinct distinct_id) from analytics.events where occurred_at_utc >= @since;", conn);
                cmd.Parameters.AddWithValue("since", sinceUtc);
                var scalar = await cmd.ExecuteScalarAsync(ct);
                if (scalar is not null && scalar != DBNull.Value)
                {
                    uniqueUsers = Convert.ToInt32(scalar);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to count unique visitors from analytics.events for daily summary.");
            }
        }

        return Results.Ok(new
        {
            periodHours = 24,
            newRegistrations,
            topupsCount,
            topupsTotalRub = totalRub,
            topupsTotalUsd = totalUsd,
            uniqueUsers,
            activeApiUsers
        });
    }
}
