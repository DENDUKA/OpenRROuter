using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

/// <summary>
/// The registration gate of <c>AddSiteVisitCompaction</c>: the worker must appear only when
/// the analytics Postgres sink is configured and the feature is switched on. Everything else
/// stays silent — there would be nothing to compact and nowhere to write.
/// </summary>
public sealed class VisitSessionCompactionRegistrationTests
{
    private const string SinkConnectionString = "Host=localhost;Database=orr_analytics_test;Username=u;Password=p";

    [Fact]
    public void AddSiteVisitCompaction_NoSinkConfigured_DoesNotRegisterWorker()
    {
        var services = Build(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "false",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:ConnectionString"] = SinkConnectionString,
        });

        HasCompactionWorker(services).Should().BeFalse();
        services.Should().NotContain(d => d.ServiceType == typeof(VisitSessionCompactor));
    }

    [Fact]
    public void AddSiteVisitCompaction_NonPostgresProvider_DoesNotRegisterWorker()
    {
        var services = Build(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "PostHog",
            ["ProductAnalytics:ConnectionString"] = SinkConnectionString,
        });

        HasCompactionWorker(services).Should().BeFalse();
    }

    [Fact]
    public void AddSiteVisitCompaction_EmptyConnectionString_DoesNotRegisterWorker()
    {
        var services = Build(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:ConnectionString"] = string.Empty,
        });

        HasCompactionWorker(services).Should().BeFalse();
    }

    [Fact]
    public void AddSiteVisitCompaction_CompactionDisabled_DoesNotRegisterWorker()
    {
        var services = Build(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:ConnectionString"] = SinkConnectionString,
            ["SiteVisits:Compaction:Enabled"] = "false",
        });

        HasCompactionWorker(services).Should().BeFalse();
        services.Should().NotContain(d => d.ServiceType == typeof(VisitSessionCompactor));
    }

    [Fact]
    public void AddSiteVisitCompaction_FullyConfigured_RegistersWorker()
    {
        var services = Build(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:ConnectionString"] = SinkConnectionString,
        });

        HasCompactionWorker(services).Should().BeTrue();
    }

    [Fact]
    public void AddSiteVisitCompaction_FullyConfigured_ResolvesCompactor()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:ConnectionString"] = SinkConnectionString,
        });

        var act = () => provider.GetRequiredService<VisitSessionCompactor>();

        act.Should().NotThrow();
    }

    /// <summary>
    /// A nonsensical batch size must not reach the compactor's argument checks and take the
    /// host down on startup — the registration clamps it instead.
    /// </summary>
    [Fact]
    public void AddSiteVisitCompaction_InvalidBatchSize_StillResolves()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:ConnectionString"] = SinkConnectionString,
            ["SiteVisits:Compaction:VisitorBatchSize"] = "0",
            ["SiteVisits:Compaction:SessionGapMinutes"] = "-1",
        });

        var act = () => provider.GetRequiredService<VisitSessionCompactor>();

        act.Should().NotThrow();
    }

    [Fact]
    public void CompactionOptions_Defaults_MatchDocumentedValues()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:ConnectionString"] = SinkConnectionString,
        });

        var options = provider.GetRequiredService<IOptions<SiteVisitOptions>>().Value.Compaction;

        options.Enabled.Should().BeTrue();
        options.IntervalMinutes.Should().Be(5);
        options.SessionGapMinutes.Should().Be(30);
        options.SafetyLagMinutes.Should().Be(5);
        options.VisitorBatchSize.Should().Be(500);
        options.MaxIterationsPerRun.Should().Be(20);
        options.MaxPagesPerSession.Should().Be(200);
        options.SplitOnUtcDayBoundary.Should().BeTrue();
    }

    [Fact]
    public void CompactionOptions_BindFromConfiguration_ReflectsValues()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:ConnectionString"] = SinkConnectionString,
            ["SiteVisits:Compaction:IntervalMinutes"] = "1",
            ["SiteVisits:Compaction:SessionGapMinutes"] = "7",
            ["SiteVisits:Compaction:SafetyLagMinutes"] = "0",
            ["SiteVisits:Compaction:VisitorBatchSize"] = "42",
            ["SiteVisits:Compaction:MaxIterationsPerRun"] = "3",
            ["SiteVisits:Compaction:MaxPagesPerSession"] = "11",
            ["SiteVisits:Compaction:SplitOnUtcDayBoundary"] = "false",
        });

        var options = provider.GetRequiredService<IOptions<SiteVisitOptions>>().Value.Compaction;

        options.IntervalMinutes.Should().Be(1);
        options.SessionGapMinutes.Should().Be(7);
        options.SafetyLagMinutes.Should().Be(0);
        options.VisitorBatchSize.Should().Be(42);
        options.MaxIterationsPerRun.Should().Be(3);
        options.MaxPagesPerSession.Should().Be(11);
        options.SplitOnUtcDayBoundary.Should().BeFalse();
    }

    private static bool HasCompactionWorker(IServiceCollection services) =>
        services.Any(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(VisitSessionCompactionWorker));

    private static IServiceCollection Build(IDictionary<string, string?> configValues)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        services.AddSiteVisitCompaction(configuration);
        return services;
    }

    private static IServiceProvider BuildProvider(IDictionary<string, string?> configValues)
    {
        var services = Build(configValues);
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
