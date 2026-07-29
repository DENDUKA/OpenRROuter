namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// One row of <c>analytics.site_visits</c> as read by the session compactor: the same
/// payload as <see cref="SiteVisit"/> plus the database identity, which is what the
/// compactor deletes by once the visit has been folded into a session row.
/// </summary>
/// <param name="Id">Value of <c>site_visits.id</c>. Also the tie-breaker used by
/// <see cref="VisitSessionizer"/> when two visits share the same timestamp.</param>
/// <param name="VisitorHash">Opaque visitor id (<see cref="VisitorIdHasher"/>). All visits
/// handed to <see cref="VisitSessionizer.Split"/> must belong to a single visitor.</param>
/// <param name="DistinctId">Set only while the visitor was signed in; equals
/// <c>analytics.events.distinct_id</c> for that user.</param>
public readonly record struct CompactableVisit(
    long Id,
    string VisitorHash,
    string? DistinctId,
    DateTimeOffset OccurredAt,
    string Path,
    string? ReferrerHost,
    string ReferrerKind,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string DeviceClass,
    bool IsAuthenticated,
    string Environment);
