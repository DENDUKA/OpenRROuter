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

    /// <summary>
    /// Settings of the background compaction that collapses raw visits into
    /// <c>analytics.visit_sessions</c>. Bound from <c>SiteVisits:Compaction</c> — a nested
    /// section rather than a root one, because it shares the very sink and table this
    /// section already describes.
    /// </summary>
    public CompactionOptions Compaction { get; set; } = new();

    /// <summary>
    /// Knobs of <c>VisitSessionCompactionWorker</c>. Deliberately independent of
    /// <see cref="Enabled"/>: turning tracking off must not leave the visits already written
    /// uncompacted, and the host that compacts (BillingService) never tracks anything.
    /// </summary>
    public sealed class CompactionOptions
    {
        /// <summary>
        /// Master switch of the compaction worker. Even when <c>true</c>, the worker is not
        /// registered unless the product analytics Postgres sink is configured.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Pause between compaction runs.</summary>
        public int IntervalMinutes { get; set; } = 5;

        /// <summary>Inactivity that starts a new session. Also half of the cutoff.</summary>
        public int SessionGapMinutes { get; set; } = 30;

        /// <summary>
        /// Extra margin on top of <see cref="SessionGapMinutes"/> when computing the cutoff:
        /// it absorbs the flush queue delay and the clock skew between WebApp replicas, which
        /// stamp <c>occurred_at_utc</c> themselves.
        /// </summary>
        public int SafetyLagMinutes { get; set; } = 5;

        /// <summary>How many visitors a single pass (one transaction) takes.</summary>
        public int VisitorBatchSize { get; set; } = 500;

        /// <summary>
        /// Upper bound on passes per run. The run also stops as soon as a pass writes no
        /// session, so this only caps the catch-up of a large backlog.
        /// </summary>
        public int MaxIterationsPerRun { get; set; } = 20;

        /// <summary>Upper bound on the stored <c>pages</c> array of one session.</summary>
        public int MaxPagesPerSession { get; set; } = 200;

        /// <summary>Whether a change of the calendar UTC day also closes a session.</summary>
        public bool SplitOnUtcDayBoundary { get; set; } = true;
    }
}
