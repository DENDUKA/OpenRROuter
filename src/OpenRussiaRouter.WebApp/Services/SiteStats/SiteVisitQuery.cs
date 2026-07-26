namespace OpenRussiaRouter.WebApp.Services.SiteStats;

/// <summary>
/// Filter + paging parameters for a <see cref="ISiteVisitReader.QueryAsync"/> call.
/// Either <see cref="UserId"/> or <see cref="VisitorHash"/> should be supplied — when
/// <see cref="UserId"/> is set, the reader resolves <c>distinct_id</c> via
/// <c>UserPseudonymizer</c>, finds the <c>visitor_hash</c> from the most recent matching
/// visit, and returns the full journey (anonymous visits before sign-up plus the
/// signed-in ones).
/// </summary>
public sealed record SiteVisitQuery(
    string? UserId = null,
    string? VisitorHash = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int PageNumber = 0,
    int PageSize = 50);

/// <summary>
/// Paged result plus the resolved identity anchors so the UI can show how the request
/// was matched (<c>visitor_hash</c> for an anonymous visitor, <c>distinct_id</c> for a
/// signed-in user, or both when a user was linked to their anonymous journey).
/// </summary>
public sealed record SiteVisitQueryResult(
    IReadOnlyList<VisitHistoryEntry> Items,
    int TotalCount,
    string? ResolvedVisitorHash,
    string? ResolvedDistinctId);