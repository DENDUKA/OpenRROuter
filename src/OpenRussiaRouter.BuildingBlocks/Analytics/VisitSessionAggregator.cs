namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Folds one session slice produced by <see cref="VisitSessionizer.Split"/> into the
/// <see cref="VisitSessionRow"/> that is written to <c>analytics.visit_sessions</c>.
/// Pure logic, no database.
/// </summary>
public static class VisitSessionAggregator
{
    /// <summary>
    /// Aggregates a session slice.
    /// </summary>
    /// <param name="slice">Visits of a single session. Ordered defensively by
    /// <c>(OccurredAt, Id)</c> so "first" and "last" below always mean by time, even if the
    /// caller assembled the slice by hand.</param>
    /// <param name="maxPages">Upper bound on the stored <c>pages</c> array; the first
    /// <paramref name="maxPages"/> page views are kept.</param>
    /// <returns>
    /// A row where: <c>EntryPath</c>/<c>ExitPath</c> are the first/last visit by time;
    /// <c>UtmSource</c>/<c>UtmMedium</c>/<c>UtmCampaign</c> are the <b>first non-empty</b>
    /// value (the acquisition source is on the landing page, later views usually drop the
    /// tags); <c>DistinctId</c> is the <b>last non-empty</b> value (the visitor may have
    /// signed in mid-session, and that identity is the useful one);
    /// <c>IsAuthenticated</c> is the OR over all visits; <c>ReferrerHost</c>,
    /// <c>ReferrerKind</c>, <c>DeviceClass</c> and <c>Environment</c> come from the first
    /// visit; <c>DurationSeconds</c> is end − start; and <c>PageViewCount</c> counts
    /// <b>all</b> visits, not the truncated <c>Pages</c> — the difference between the two
    /// is how truncation is detected downstream.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="slice"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="slice"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPages"/> is negative.</exception>
    public static VisitSessionRow Aggregate(IEnumerable<CompactableVisit> slice, int maxPages)
    {
        ArgumentNullException.ThrowIfNull(slice);
        if (maxPages < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPages), maxPages, "Page limit must not be negative.");
        }

        var ordered = slice.OrderBy(static v => v.OccurredAt).ThenBy(static v => v.Id).ToList();
        if (ordered.Count == 0)
        {
            throw new ArgumentException("A session must contain at least one visit.", nameof(slice));
        }

        var first = ordered[0];
        var last = ordered[^1];

        string? distinctId = null;
        string? utmSource = null;
        string? utmMedium = null;
        string? utmCampaign = null;
        var isAuthenticated = false;

        foreach (var visit in ordered)
        {
            // Last non-empty wins: a visitor who signs in halfway through the session is
            // more useful attached to their user than left anonymous.
            if (!string.IsNullOrWhiteSpace(visit.DistinctId))
            {
                distinctId = visit.DistinctId;
            }

            // First non-empty wins for the campaign tags.
            utmSource ??= NullIfBlank(visit.UtmSource);
            utmMedium ??= NullIfBlank(visit.UtmMedium);
            utmCampaign ??= NullIfBlank(visit.UtmCampaign);

            isAuthenticated |= visit.IsAuthenticated;
        }

        var pageCount = Math.Min(ordered.Count, maxPages);
        var pages = new List<VisitSessionPage>(pageCount);
        for (var i = 0; i < pageCount; i++)
        {
            pages.Add(new VisitSessionPage(ordered[i].OccurredAt, ordered[i].Path));
        }

        var duration = last.OccurredAt - first.OccurredAt;

        return new VisitSessionRow(
            VisitorHash: first.VisitorHash,
            DistinctId: distinctId,
            StartedAt: first.OccurredAt,
            EndedAt: last.OccurredAt,
            DurationSeconds: (int)duration.TotalSeconds,
            // Counted over the whole slice on purpose: `pages` above may be truncated, and
            // `PageViewCount > Pages.Count` is the only marker of that truncation.
            PageViewCount: ordered.Count,
            EntryPath: first.Path,
            ExitPath: last.Path,
            ReferrerHost: first.ReferrerHost,
            ReferrerKind: first.ReferrerKind,
            UtmSource: utmSource,
            UtmMedium: utmMedium,
            UtmCampaign: utmCampaign,
            DeviceClass: first.DeviceClass,
            IsAuthenticated: isAuthenticated,
            Pages: pages,
            Environment: first.Environment);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
