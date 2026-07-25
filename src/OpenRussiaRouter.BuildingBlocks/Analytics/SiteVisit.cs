namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// A single page view on the public site or the cabinet, staged for
/// <c>analytics.site_visits</c>. Carries no raw IP: the visitor is represented by the
/// opaque <paramref name="VisitorHash"/> produced by <see cref="VisitorIdHasher"/>.
/// <paramref name="DistinctId"/> is set only while the visitor is signed in and matches
/// <c>analytics.events.distinct_id</c> for that user.
/// </summary>
public readonly record struct SiteVisit(
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

/// <summary>Values stored in <c>site_visits.device_class</c>.</summary>
public static class SiteVisitDeviceClasses
{
    public const string Desktop = "desktop";
    public const string Mobile = "mobile";
    public const string Tablet = "tablet";
    public const string Bot = "bot";
}

/// <summary>
/// Values stored in <c>site_visits.referrer_kind</c>. The dashboard groups traffic by
/// this coarse bucket and drills down into <c>referrer_host</c> only inside it.
/// </summary>
public static class SiteVisitReferrerKinds
{
    /// <summary>No <c>Referer</c> header: typed URL, bookmark, or a stripped referrer.</summary>
    public const string Direct = "direct";

    /// <summary>Navigation inside our own site — not an acquisition source.</summary>
    public const string Internal = "internal";

    public const string Search = "search";
    public const string Social = "social";

    /// <summary>Any other external site.</summary>
    public const string External = "external";
}
