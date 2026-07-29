namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Splits the raw page views of a single visitor into sessions ("sittings").
/// Pure logic, no database: the compactor reads the rows, calls this, and writes one
/// <c>analytics.visit_sessions</c> row per slice.
/// </summary>
/// <remarks>
/// The session boundary rule lives here and only here — the deliberate reason the
/// compaction is done in C# instead of a window-function query, where the same rule would
/// have to be duplicated for unit tests and would silently drift.
/// </remarks>
public static class VisitSessionizer
{
    /// <summary>
    /// Groups <paramref name="visits"/> into consecutive session slices.
    /// </summary>
    /// <param name="visits">Page views of <b>one</b> visitor, in any order. Mixing several
    /// visitors is a caller error: the slices would interleave.</param>
    /// <param name="gap">Inactivity that starts a new session. The comparison is strict —
    /// a pause of exactly <paramref name="gap"/> stays inside the same session, only
    /// <c>delta &gt; gap</c> splits.</param>
    /// <param name="splitOnUtcDayBoundary">When <c>true</c> (default) a change of the
    /// calendar UTC day also starts a new session, no matter how small the gap is. This is
    /// what bounds a session: without it a crawler polling every five minutes for a week
    /// produces one session that never closes, is recompacted on every run and grows its
    /// <c>pages</c> array without limit.</param>
    /// <returns>Slices ordered by time, each ordered by <c>(OccurredAt, Id)</c>. An empty
    /// input produces an empty list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="visits"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gap"/> is negative.</exception>
    public static IReadOnlyList<IReadOnlyList<CompactableVisit>> Split(
        IEnumerable<CompactableVisit> visits,
        TimeSpan gap,
        bool splitOnUtcDayBoundary = true)
    {
        ArgumentNullException.ThrowIfNull(visits);
        if (gap < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(gap), gap, "Session gap must not be negative.");
        }

        // Sorted here on purpose: the caller is not required to hand over ordered rows, and
        // the boundary rule is only meaningful on a chronological sequence. Ties on
        // OccurredAt are broken by the insertion order of the source table (id).
        var ordered = visits.OrderBy(static v => v.OccurredAt).ThenBy(static v => v.Id).ToList();
        if (ordered.Count == 0)
        {
            return Array.Empty<IReadOnlyList<CompactableVisit>>();
        }

        var slices = new List<IReadOnlyList<CompactableVisit>>();
        var current = new List<CompactableVisit> { ordered[0] };

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var visit = ordered[i];

            if (StartsNewSession(previous, visit, gap, splitOnUtcDayBoundary))
            {
                slices.Add(current);
                current = new List<CompactableVisit>();
            }

            current.Add(visit);
        }

        slices.Add(current);
        return slices;
    }

    private static bool StartsNewSession(
        CompactableVisit previous,
        CompactableVisit visit,
        TimeSpan gap,
        bool splitOnUtcDayBoundary)
    {
        // Strictly greater: a pause of exactly `gap` belongs to the same session.
        if (visit.OccurredAt - previous.OccurredAt > gap)
        {
            return true;
        }

        // Compared in UTC, so a visit carrying a non-zero offset lands on the same calendar
        // day as the stored timestamptz value.
        return splitOnUtcDayBoundary
            && visit.OccurredAt.UtcDateTime.Date != previous.OccurredAt.UtcDateTime.Date;
    }
}
