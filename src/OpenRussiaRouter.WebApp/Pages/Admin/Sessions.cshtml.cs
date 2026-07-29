using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.SiteStats;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

/// <summary>
/// Admin-only listing of collapsed visitor sessions. Reads <c>analytics.visit_sessions</c>
/// via <see cref="VisitSessionReader"/> — one row per visit session, written by the compaction
/// worker in BillingService. This is the long-lived view of site traffic; <c>/admin/visitors</c>
/// keeps showing the raw, not-yet-compacted tail of <c>analytics.site_visits</c>.
/// </summary>
/// <remarks>
/// Filtering mirrors <see cref="VisitorsModel"/> deliberately (same query-string names, same
/// <c>userId → distinct_id → visitor_hash</c> resolution), so an admin can jump between the two
/// pages without rewriting the URL. The one addition is <c>bots</c>: bots are compacted like
/// everyone else so the "bot share" metric stays computable, but they drown the listing and are
/// hidden unless asked for.
/// </remarks>
[Authorize(Roles = "Admin")]
public class SessionsModel(
    VisitSessionReader reader,
    AuthProvider authProvider,
    UserPseudonymizer pseudonymizer,
    ILogger<SessionsModel> logger) : PageModelBase
{
    private const int PageSize = 50;

    public IReadOnlyList<VisitSessionEntry> Records { get; set; } = [];
    public bool AnalyticsEnabled { get; set; } = true;
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int TotalPages => TotalCount <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 0;
    public bool HasNextPage => PageNumber + 1 < TotalPages;

    public string? ResolvedVisitorHash { get; set; }
    public string? ResolvedDistinctId { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Reverse map <c>distinct_id → display name</c>, built exactly like
    /// <see cref="VisitorsModel"/> does: enumerate users via
    /// <see cref="AuthProvider.ListUsersAsync"/>, compute each user's <c>distinct_id</c> with
    /// the same <see cref="UserPseudonymizer"/> the write side uses. Empty when auth is
    /// unreachable — the page still renders, just without names.
    /// </summary>
    public IReadOnlyDictionary<string, string> UserNamesByDistinctId { get; set; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? VisitorHash { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }

    /// <summary>Checkbox "Show bots" — off by default, see the remarks on the class.</summary>
    [BindProperty(SupportsGet = true, Name = "bots")] public bool IncludeBots { get; set; }

    [BindProperty(SupportsGet = true)] public int P { get; set; }

    public async Task OnGetAsync()
    {
        PageNumber = Math.Max(0, P);

        if (!reader.IsEnabled)
        {
            AnalyticsEnabled = false;
            return;
        }

        DateTimeOffset? fromUtc = ParseDate(From);
        DateTimeOffset? toUtc = ParseDate(To);

        var query = new VisitSessionQuery(
            UserId: UserId,
            VisitorHash: VisitorHash,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            IncludeBots: IncludeBots,
            PageNumber: PageNumber,
            PageSize: PageSize);

        try
        {
            var result = await reader.QueryAsync(query);
            Records = result.Items;
            TotalCount = result.TotalCount;
            ResolvedVisitorHash = result.ResolvedVisitorHash;
            ResolvedDistinctId = result.ResolvedDistinctId;

            UserNamesByDistinctId = await BuildUserNameMapAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load visit sessions.");
            ErrorMessage = "Failed to load visit sessions.";
        }
    }

    /// <summary>
    /// Resolves a session's <c>distinct_id</c> to the user's display name. Returns
    /// <c>null</c> for anonymous sessions or when the user could not be matched (deleted
    /// account, auth unreachable on this page load).
    /// </summary>
    public string? GetUserName(VisitSessionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.DistinctId is null
            ? null
            : UserNamesByDistinctId.TryGetValue(entry.DistinctId, out var name)
                ? name
                : null;
    }

    /// <summary>
    /// Formats a session length as <c>h:mm:ss</c> / <c>m:ss</c> / <c>Ns</c>. Single-page
    /// sessions have a duration of zero by construction (start equals end) and read as
    /// <c>0s</c> rather than an empty cell.
    /// </summary>
    public static string FormatDuration(int seconds)
    {
        if (seconds <= 0)
        {
            return "0s";
        }

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}")
            : span.TotalMinutes >= 1
                ? string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:D2}")
                : string.Create(CultureInfo.InvariantCulture, $"{span.Seconds}s");
    }

    public bool HasActiveFilter() =>
        !string.IsNullOrWhiteSpace(UserId) || !string.IsNullOrWhiteSpace(VisitorHash) ||
        !string.IsNullOrWhiteSpace(From) || !string.IsNullOrWhiteSpace(To) || IncludeBots;

    public string BuildPageUrl(int page)
    {
        var parts = new List<string>();
        if (page > 0) parts.Add($"p={page}");
        if (!string.IsNullOrWhiteSpace(UserId))
            parts.Add($"userId={Uri.EscapeDataString(UserId!.Trim())}");
        if (!string.IsNullOrWhiteSpace(VisitorHash))
            parts.Add($"visitorHash={Uri.EscapeDataString(VisitorHash!.Trim())}");
        if (!string.IsNullOrWhiteSpace(From))
            parts.Add($"from={Uri.EscapeDataString(From!.Trim())}");
        if (!string.IsNullOrWhiteSpace(To))
            parts.Add($"to={Uri.EscapeDataString(To!.Trim())}");
        if (IncludeBots)
            parts.Add("bots=true");
        return "/admin/sessions" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }

    internal static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Builds <see cref="UserNamesByDistinctId"/>. Failures (auth unreachable, pseudonymizer
    /// misconfigured) are swallowed — the page renders without names rather than crashing,
    /// matching how the rest of the admin pages tolerate per-call provider failures.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> BuildUserNameMapAsync()
    {
        try
        {
            var users = await authProvider.ListUsersAsync();
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var u in users)
            {
                var distinctId = pseudonymizer.ComputeDistinctId(u.Id);
                map[distinctId] = string.IsNullOrWhiteSpace(u.DisplayName)
                    ? (u.Email ?? u.Id)
                    : u.DisplayName;
            }
            return map;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to load users for visit-session name resolution.");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
