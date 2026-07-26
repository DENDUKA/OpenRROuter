namespace OpenRussiaRouter.WebApp.Services.SiteStats;

/// <summary>
/// One row of <c>analytics.site_visits</c> materialised for the admin visitor-history page.
/// Carries only the columns the UI needs — the raw id is kept so the admin can correlate
/// rows with the underlying table if necessary.
/// </summary>
public sealed record VisitHistoryEntry(
    long Id,
    string VisitorHash,
    string? DistinctId,
    DateTimeOffset OccurredAtUtc,
    string Path,
    string? ReferrerHost,
    string ReferrerKind,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string DeviceClass,
    bool IsAuthenticated,
    string Environment);
