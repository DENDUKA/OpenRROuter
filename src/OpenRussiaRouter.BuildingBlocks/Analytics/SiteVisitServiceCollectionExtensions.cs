using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
}
