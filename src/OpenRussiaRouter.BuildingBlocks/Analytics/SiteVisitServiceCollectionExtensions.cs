using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Wires site-visit tracking into the host that serves the site (WebApp). Must be called
/// after <see cref="ProductAnalyticsServiceCollectionExtensions.AddProductAnalytics"/>:
/// visits share its Postgres sink, HMAC salt and environment label, and the signed-in
/// branch needs the same <see cref="UserPseudonymizer"/> so a visit can be matched to the
/// user's <c>distinct_id</c>.
/// </summary>
public static class SiteVisitServiceCollectionExtensions
{
    public static IServiceCollection AddSiteVisitTracking(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SiteVisitOptions>()
            .Bind(configuration.GetSection(SiteVisitOptions.SectionName));

        var visitOptions = configuration.GetSection(SiteVisitOptions.SectionName).Get<SiteVisitOptions>()
            ?? new SiteVisitOptions();
        var analyticsOptions = configuration.GetSection(ProductAnalyticsOptions.SectionName).Get<ProductAnalyticsOptions>()
            ?? new ProductAnalyticsOptions();

        // No sink configured means there is nowhere to write visits — stay off rather than
        // failing startup, exactly like the product analytics pipeline does.
        var sinkAvailable =
            analyticsOptions.Enabled &&
            string.Equals(analyticsOptions.Provider, "Postgres", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(analyticsOptions.ConnectionString) &&
            !string.IsNullOrWhiteSpace(analyticsOptions.IdentitySalt);

        if (!visitOptions.Enabled || !sinkAvailable)
        {
            return services;
        }

        services.TryAddSingleton<VisitorIdHasher>(_ => new VisitorIdHasher(analyticsOptions));
        services.TryAddSingleton<SiteVisitQueue>();
        services.TryAddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(analyticsOptions.ConnectionString!));
        services.AddHostedService<SiteVisitFlushWorker>();

        return services;
    }

    /// <summary>
    /// Registers the background compaction of <c>analytics.site_visits</c> into
    /// <c>analytics.visit_sessions</c>. Separate from
    /// <see cref="AddSiteVisitTracking"/> on purpose: tracking belongs to every host that
    /// serves pages (WebApp, replicated), compaction belongs to exactly one host —
    /// BillingService, which owns the analytics database and runs as a single replica.
    /// </summary>
    /// <remarks>
    /// The gate is the same Postgres sink check as tracking's, minus the HMAC salt: the
    /// compactor only moves rows and never hashes anything. <c>SiteVisits:Enabled</c> is not
    /// part of the gate either — switching tracking off must not leave the visits already
    /// written to pile up uncompacted.
    /// </remarks>
    public static IServiceCollection AddSiteVisitCompaction(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SiteVisitOptions>()
            .Bind(configuration.GetSection(SiteVisitOptions.SectionName));

        var visitOptions = configuration.GetSection(SiteVisitOptions.SectionName).Get<SiteVisitOptions>()
            ?? new SiteVisitOptions();
        var analyticsOptions = configuration.GetSection(ProductAnalyticsOptions.SectionName).Get<ProductAnalyticsOptions>()
            ?? new ProductAnalyticsOptions();

        var sinkAvailable =
            analyticsOptions.Enabled &&
            string.Equals(analyticsOptions.Provider, "Postgres", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(analyticsOptions.ConnectionString);

        if (!visitOptions.Compaction.Enabled || !sinkAvailable)
        {
            return services;
        }

        // Normally already registered by AddProductAnalytics — the analytics data source is a
        // single shared singleton, and a second one would open a second pool to the same
        // database.
        services.TryAddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(analyticsOptions.ConnectionString!));

        services.TryAddSingleton<VisitSessionCompactor>(sp => new VisitSessionCompactor(
            sp.GetRequiredService<NpgsqlDataSource>(),
            BuildCompactionSettings(sp.GetRequiredService<IOptions<SiteVisitOptions>>().Value.Compaction),
            sp.GetRequiredService<ILogger<VisitSessionCompactor>>()));

        services.AddHostedService<VisitSessionCompactionWorker>();

        return services;
    }

    /// <summary>
    /// Maps configuration onto the compactor's settings, clamping nonsense values instead of
    /// letting the compactor's argument checks fail host startup: a typo in an analytics knob
    /// must not take BillingService down.
    /// </summary>
    private static VisitSessionCompactionSettings BuildCompactionSettings(SiteVisitOptions.CompactionOptions options) =>
        new(
            SessionGap: TimeSpan.FromMinutes(Math.Max(0, options.SessionGapMinutes)),
            SafetyLag: TimeSpan.FromMinutes(Math.Max(0, options.SafetyLagMinutes)),
            VisitorBatchSize: options.VisitorBatchSize > 0 ? options.VisitorBatchSize : 500,
            MaxPagesPerSession: Math.Max(0, options.MaxPagesPerSession),
            SplitOnUtcDayBoundary: options.SplitOnUtcDayBoundary);
}
