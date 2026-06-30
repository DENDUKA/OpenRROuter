namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Configuration bound from the <c>ProductAnalytics</c> configuration section. The
/// section is parsed unconditionally; whether events actually flow depends on
/// <see cref="Enabled"/>, <see cref="Provider"/> and <see cref="ConnectionString"/> (see
/// <see cref="ProductAnalyticsServiceCollectionExtensions"/>).
/// </summary>
public sealed class ProductAnalyticsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ProductAnalytics";

    /// <summary>Master switch. When <c>false</c> the pipeline resolves to <c>NoOp</c>.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Sink identifier. <c>Postgres</c> enables the internal sink; <c>PostHog</c>
    /// is reserved for a future external sink. Defaults to <c>PostHog</c>.</summary>
    public string Provider { get; set; } = "PostHog";

    /// <summary>Sink host URL (e.g. PostHog Cloud region or self-hosted host).</summary>
    public string? HostUrl { get; set; }

    /// <summary>Project token (<c>phc_...</c> for PostHog). Empty/missing means NoOp.</summary>
    public string? ProjectToken { get; set; }

    /// <summary>Connection string for the internal Postgres analytics database.
    /// Required when <see cref="Provider"/> is <c>Postgres</c>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>HMAC key used to pseudonymize <c>UserId</c>. Treat as a secret;
    /// rotating it invalidates existing <c>distinct_id</c> mappings.</summary>
    public string? IdentitySalt { get; set; }

    /// <summary>Environment label attached to every event (e.g. <c>production</c>).</summary>
    public string? Environment { get; set; }

    /// <summary>Maximum number of events held in the in-memory queue. Writes are
    /// dropped when the queue is full.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>Number of events inserted into Postgres in a single batch.</summary>
    public int BatchSize { get; set; } = 100;
}