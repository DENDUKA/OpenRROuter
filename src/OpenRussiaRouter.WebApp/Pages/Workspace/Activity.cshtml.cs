using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.Contracts.Auth;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class ActivityModel : PageModelBase
{
    private readonly BillingProvider _billingClient;
    private readonly AuthProvider _authClient;
    private readonly IStringLocalizer<SharedResource> _localizer;

    // Tab & filters
    [FromQuery(Name = "tab")] public string Tab { get; set; } = "details";
    [FromQuery(Name = "from")] public string? FromDate { get; set; }
    [FromQuery(Name = "to")] public string? ToDate { get; set; }
    [FromQuery(Name = "model")] public string? ModelFilter { get; set; }
    [FromQuery(Name = "key")] public string? KeyFilter { get; set; }
    [FromQuery(Name = "after")] public string? After { get; set; }
    [FromQuery(Name = "period")] public string Period { get; set; } = "7d";

    // Number of records per page in the Details tab.
    public const int PageSize = 100;

    // Details tab output
    public IReadOnlyList<ActivityByTokenInfo> Activity { get; set; } = [];
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }

    // Overview tab output
    public ActivitySummaryInfo? Summary { get; set; }

    // Shared
    public IReadOnlyList<ApiKeySummary> ApiKeys { get; set; } = [];
    public IReadOnlyList<string> AvailableModels { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public ActivityModel(BillingProvider BillingProvider, AuthProvider AuthProvider, IStringLocalizer<SharedResource> localizer)
    {
        _billingClient = BillingProvider;
        _authClient = AuthProvider;
        _localizer = localizer;
    }

    public async Task OnGetAsync()
    {
        try
        {
            ApiKeys = await _authClient.ListApiKeysAsync(User.GetUserId(), includeDeleted: false, HttpContext.RequestAborted);
            var keyIds = ApiKeys.Select(k => k.Id).ToArray();

            if (Tab == "overview")
            {
                var (fromUtc, toUtc) = ParsePeriod(Period);
                Summary = await _billingClient.GetActivitySummaryAsync(keyIds, fromUtc, toUtc, HttpContext.RequestAborted);
                AvailableModels = Summary.Models.Select(m => m.ModelId).OrderBy(id => id).ToList();
            }
            else
            {
                DateTimeOffset? fromUtc = null, toUtc = null;
                if (!string.IsNullOrEmpty(FromDate) && DateOnly.TryParseExact(FromDate, "yyyy-MM-dd", out var fd))
                    fromUtc = fd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
                if (!string.IsNullOrEmpty(ToDate) && DateOnly.TryParseExact(ToDate, "yyyy-MM-dd", out var td))
                    toUtc = td.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local).ToUniversalTime();

                var afterCursor = string.IsNullOrEmpty(After) 
                    ? null 
                    : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(After));

                var activityTask = _billingClient.ListActivityByTokenAsync(
                    keyIds,
                    fromUtc: fromUtc,
                    toUtc: toUtc,
                    modelIdFilter: string.IsNullOrEmpty(ModelFilter) ? null : ModelFilter,
                    apiKeyIdFilter: string.IsNullOrEmpty(KeyFilter) ? null : KeyFilter,
                    afterCursor: afterCursor,
                    pageSize: PageSize,
                    cancellationToken: HttpContext.RequestAborted);

                var summaryTask = _billingClient.GetActivitySummaryAsync(keyIds, fromUtc, toUtc, HttpContext.RequestAborted);

                await Task.WhenAll(activityTask, summaryTask);

                var (items, hasMore, nextCursor) = await activityTask;
                Activity = items;
                HasMore = hasMore;
                NextCursor = nextCursor is null
                    ? null
                    : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(nextCursor));

                var summary = await summaryTask;
                AvailableModels = summary.Models.Select(m => m.ModelId).OrderBy(id => id).ToList();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localizer["Activity_LoadFailed", ex.Message];
        }
    }

    public string BuildDetailsUrl(string? after = null)
    {
        var qs = new List<string> { "tab=details" };
        if (!string.IsNullOrEmpty(FromDate)) qs.Add($"from={FromDate}");
        if (!string.IsNullOrEmpty(ToDate)) qs.Add($"to={ToDate}");
        if (!string.IsNullOrEmpty(ModelFilter)) qs.Add($"model={Uri.EscapeDataString(ModelFilter)}");
        if (!string.IsNullOrEmpty(KeyFilter)) qs.Add($"key={Uri.EscapeDataString(KeyFilter)}");
        if (!string.IsNullOrEmpty(after)) qs.Add($"after={Uri.EscapeDataString(after)}");
        return "/workspace/activity?" + string.Join("&", qs);
    }

    public string BuildOverviewUrl(string period) =>
        $"/workspace/activity?tab=overview&period={period}";

    private static (DateTimeOffset? From, DateTimeOffset? To) ParsePeriod(string period)
    {
        var now = DateTimeOffset.UtcNow;
        return period switch
        {
            "today" => (now.Date.ToUniversalTime(), now),
            "24h" => (now.AddHours(-24), now),
            "30d" => (now.AddDays(-30), now),
            _ => (now.AddDays(-7), now)
        };
    }
}
