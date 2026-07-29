using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.WebApp.Services.SiteStats;

/// <summary>
/// One row of <c>analytics.visit_sessions</c> materialised for the admin session-list page.
/// Carries the aggregate columns only — the <c>pages</c> jsonb array is deliberately left
/// out of the listing query (it can be large and nothing in the list renders it) and is
/// loaded per session by <see cref="VisitSessionReader.GetByIdAsync"/>.
/// </summary>
/// <param name="PageViewCount">Page views over the whole session, counted before the stored
/// <c>pages</c> array was truncated — see <see cref="VisitSessionDetails.IsTruncated"/>.</param>
public sealed record VisitSessionEntry(
    long Id,
    string VisitorHash,
    string? DistinctId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    int DurationSeconds,
    int PageViewCount,
    string EntryPath,
    string ExitPath,
    string? ReferrerHost,
    string ReferrerKind,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string DeviceClass,
    bool IsAuthenticated,
    string Environment);

/// <summary>
/// A single session plus its parsed page list, for the session-details page.
/// </summary>
/// <param name="Pages">The <c>pages</c> jsonb array deserialized into the same
/// <see cref="VisitSessionPage"/> shape the compactor wrote (<c>{"t":…,"p":…}</c>), ordered
/// as stored — chronologically. Empty when the column could not be parsed; the reader logs
/// and degrades instead of failing the page.</param>
public sealed record VisitSessionDetails(
    VisitSessionEntry Session,
    IReadOnlyList<VisitSessionPage> Pages)
{
    /// <summary>
    /// <c>true</c> when the compactor cut the page list at its <c>MaxPagesPerSession</c>
    /// bound: there is no flag column, the mismatch between the counted views and the stored
    /// array is the only marker, so the UI computes it here rather than in the view.
    /// </summary>
    public bool IsTruncated => Session.PageViewCount > Pages.Count;
}
