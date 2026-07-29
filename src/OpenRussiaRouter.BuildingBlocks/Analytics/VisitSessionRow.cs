using System.Text.Json.Serialization;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// One entry of the <c>visit_sessions.pages</c> jsonb array: when a page was opened and
/// which path it was. The short property names are the on-disk shape
/// (<c>{"t":"2026-07-27T10:00:03Z","p":"/models"}</c>) — the array is stored per session
/// and never searched inside, so it is kept compact.
/// </summary>
public readonly record struct VisitSessionPage(
    [property: JsonPropertyName("t")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("p")] string Path);

/// <summary>
/// An aggregated visitor session — the in-memory mirror of one <c>analytics.visit_sessions</c>
/// row, produced by <see cref="VisitSessionAggregator.Aggregate"/> from a slice returned by
/// <see cref="VisitSessionizer.Split"/>.
/// </summary>
/// <remarks>
/// The row deliberately carries no source visit ids: it maps column-for-column onto the
/// table. The compactor keeps the slice next to the row it produced and deletes the
/// visits from there.
/// </remarks>
/// <param name="PageViewCount">Number of visits in the whole session, counted before
/// <paramref name="Pages"/> is truncated. <c>PageViewCount &gt; Pages.Count</c> is exactly
/// how the admin page detects that the page list was cut.</param>
/// <param name="Pages">Session path, truncated to the aggregator's <c>maxPages</c>.</param>
public sealed record VisitSessionRow(
    string VisitorHash,
    string? DistinctId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
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
    IReadOnlyList<VisitSessionPage> Pages,
    string Environment);
