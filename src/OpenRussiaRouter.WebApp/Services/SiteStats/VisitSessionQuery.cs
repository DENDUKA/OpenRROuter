namespace OpenRussiaRouter.WebApp.Services.SiteStats;

/// <summary>
/// Filter + paging parameters for <see cref="VisitSessionReader.QueryAsync"/>. Mirrors
/// <see cref="SiteVisitQuery"/>, but the time window applies to <c>started_at_utc</c> and
/// there is an extra bot switch.
/// </summary>
/// <remarks>
/// When <see cref="UserId"/> is set, the reader resolves <c>distinct_id</c> via
/// <c>UserPseudonymizer</c>, takes the <c>visitor_hash</c> off the most recent session that
/// carries it, and lists every session of that visitor — the anonymous ones before sign-up
/// included. <see cref="VisitorHash"/> is the direct alternative. With neither filter set the
/// reader returns the latest sessions across everyone, so the page is not empty on first open.
/// </remarks>
/// <param name="IncludeBots">Bots are compacted like everyone else (the "bot share" metric
/// has to stay computable), but they drown the admin listing, so they are excluded by
/// default with <c>device_class &lt;&gt; 'bot'</c>.</param>
public sealed record VisitSessionQuery(
    string? UserId = null,
    string? VisitorHash = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    bool IncludeBots = false,
    int PageNumber = 0,
    int PageSize = 50);

/// <summary>
/// Paged result plus the resolved identity anchors, so the UI can show how the request was
/// matched (<c>visitor_hash</c> for an anonymous visitor, <c>distinct_id</c> for a signed-in
/// user, or both when a user was linked back to their anonymous journey).
/// </summary>
public sealed record VisitSessionQueryResult(
    IReadOnlyList<VisitSessionEntry> Items,
    int TotalCount,
    string? ResolvedVisitorHash,
    string? ResolvedDistinctId);
