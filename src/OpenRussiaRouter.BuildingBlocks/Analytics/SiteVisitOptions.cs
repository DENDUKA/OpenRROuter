namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Configuration bound from the <c>SiteVisits</c> section. The sink itself (connection
/// string, HMAC salt, environment label) is shared with
/// <see cref="ProductAnalyticsOptions"/> — visits live in the same analytics database and
/// must use the same salt, otherwise a signed-in visit could not be matched to the user's
/// <c>distinct_id</c>.
/// </summary>
public sealed class SiteVisitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SiteVisits";

    /// <summary>
    /// Master switch. Even when <c>true</c>, tracking stays off unless the product
    /// analytics Postgres sink is configured — there would be nowhere to write.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum number of visits held in memory. Writes are dropped when full,
    /// so a slow database can never block page rendering.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>Number of visits inserted into Postgres in a single batch.</summary>
    public int BatchSize { get; set; } = 200;
}
