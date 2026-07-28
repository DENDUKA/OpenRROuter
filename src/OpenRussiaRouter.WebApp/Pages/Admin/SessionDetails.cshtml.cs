using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.SiteStats;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

/// <summary>
/// Admin-only detail view of one collapsed visitor session: the aggregate header plus the
/// stored <c>pages</c> path. Route is declared explicitly on the view
/// (<c>@page "/admin/sessions/{id:long}"</c>) — a file-derived route would carry no id.
/// </summary>
/// <remarks>
/// Rows come from <see cref="VisitSessionReader.GetByIdAsync"/>, which returns <c>null</c> both
/// for an unknown id and for a disabled analytics sink. The two cases must not collapse into
/// one: a disabled sink renders the same banner the listing page shows, an unknown id answers
/// <c>404</c>. Answering <c>200</c> with an empty page for a missing row is the defect T-064
/// fixed elsewhere in the admin area.
/// </remarks>
[Authorize(Roles = "Admin")]
public class SessionDetailsModel(
    VisitSessionReader reader,
    AuthProvider authProvider,
    UserPseudonymizer pseudonymizer,
    ILogger<SessionDetailsModel> logger) : PageModelBase
{
    /// <summary>The loaded session, or <c>null</c> when the sink is disabled or loading failed.</summary>
    public VisitSessionDetails? Details { get; private set; }

    public bool AnalyticsEnabled { get; private set; } = true;

    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Display name behind the session's <c>distinct_id</c>, or <c>null</c> for anonymous
    /// sessions and when the user could not be matched (deleted account, auth unreachable).
    /// </summary>
    public string? UserName { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        // Disabled sink is not a missing session — banner, not 404.
        if (!reader.IsEnabled)
        {
            AnalyticsEnabled = false;
            return Page();
        }

        VisitSessionDetails? details;
        try
        {
            details = await reader.GetByIdAsync(id, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load visit session {SessionId}.", id);
            ErrorMessage = "Failed to load the visit session.";
            return Page();
        }

        return await ApplyResultAsync(details);
    }

    /// <summary>
    /// Turns the reader's result into a page result: a missing row is a <c>404</c>, a present
    /// one populates the model and resolves the user name. Separate from
    /// <see cref="OnGetAsync"/> so the not-found contract is testable without a live sink.
    /// </summary>
    internal async Task<IActionResult> ApplyResultAsync(VisitSessionDetails? details)
    {
        if (details is null)
        {
            return NotFound();
        }

        Details = details;
        UserName = await ResolveUserNameAsync(details.Session.DistinctId);
        return Page();
    }

    /// <summary>
    /// Caption for a truncated page list. Truncation has no flag column: the compactor cuts
    /// <c>pages</c> at its <c>MaxPagesPerSession</c> bound while <c>page_view_count</c> keeps
    /// counting every view, so the mismatch is the only marker (see
    /// <see cref="VisitSessionDetails.IsTruncated"/>). <c>null</c> when nothing was cut.
    /// </summary>
    public string? TruncationNote => Details is { IsTruncated: true } d
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"Showing the first {d.Pages.Count} of {d.Session.PageViewCount} page views — the compactor caps the stored path.")
        : null;

    /// <summary>
    /// Formats a page view's position inside the session as <c>+mm:ss</c>, or
    /// <c>+h:mm:ss</c> once the session passes an hour. Offsets before the session start
    /// (a late flush merged into an already-written session) clamp to <c>+00:00</c> rather
    /// than rendering a negative time.
    /// </summary>
    public static string FormatOffset(DateTimeOffset sessionStart, DateTimeOffset pageAt)
    {
        var delta = pageAt - sessionStart;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        return delta.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"+{(int)delta.TotalHours}:{delta.Minutes:D2}:{delta.Seconds:D2}")
            : string.Create(CultureInfo.InvariantCulture, $"+{delta.Minutes:D2}:{delta.Seconds:D2}");
    }

    /// <summary>
    /// Resolves one <c>distinct_id</c> to a display name. The id is an HMAC and cannot be
    /// inverted, so the users are enumerated and their <c>distinct_id</c> recomputed with the
    /// same <see cref="UserPseudonymizer"/> the write side uses — the single-user counterpart of
    /// <c>SessionsModel.BuildUserNameMapAsync</c>. Failures are swallowed: the page renders
    /// without a name rather than 500-ing on an unrelated auth outage.
    /// </summary>
    private async Task<string?> ResolveUserNameAsync(string? distinctId)
    {
        if (string.IsNullOrEmpty(distinctId))
        {
            return null;
        }

        try
        {
            var users = await authProvider.ListUsersAsync();
            foreach (var u in users)
            {
                if (!string.Equals(pseudonymizer.ComputeDistinctId(u.Id), distinctId, StringComparison.Ordinal))
                {
                    continue;
                }

                return string.IsNullOrWhiteSpace(u.DisplayName) ? (u.Email ?? u.Id) : u.DisplayName;
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to resolve the user behind a visit session.");
            return null;
        }
    }
}
