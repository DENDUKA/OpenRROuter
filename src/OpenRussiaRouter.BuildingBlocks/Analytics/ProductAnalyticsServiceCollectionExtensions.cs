using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Wires the product analytics pipeline into the host DI container:
/// <list type="bullet">
///   <item><description>binds <see cref="ProductAnalyticsOptions"/> from configuration,</description></item>
///   <item><description>registers the HMAC <see cref="UserPseudonymizer"/> and the
/// <see cref="AnalyticsEventBuilder"/>,</description></item>
///   <item><description>resolves <see cref="IProductAnalytics"/> to <see cref="PostgresProductAnalytics"/>
/// when <c>Enabled=true</c>, <c>Provider=Postgres</c> and <c>ConnectionString</c> are configured;
/// otherwise it falls back to <see cref="NoOpProductAnalytics"/>.</description></item>
/// </list>
/// </summary>
public static class ProductAnalyticsServiceCollectionExtensions
{
    public static IServiceCollection AddProductAnalytics(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ProductAnalyticsOptions>()
            .Bind(configuration.GetSection(ProductAnalyticsOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ProductAnalyticsOptions>, ProductAnalyticsOptionsValidator>();

        services.TryAddSingleton<UserPseudonymizer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ProductAnalyticsOptions>>().Value;
            return new UserPseudonymizer(options);
        });
        services.TryAddSingleton<AnalyticsEventBuilder>(sp =>
        {
            var pseudonymizer = sp.GetRequiredService<UserPseudonymizer>();
            var options = sp.GetRequiredService<IOptions<ProductAnalyticsOptions>>().Value;
            return new AnalyticsEventBuilder(pseudonymizer, options.Environment);
        });

        var options = configuration.GetSection(ProductAnalyticsOptions.SectionName).Get<ProductAnalyticsOptions>()
            ?? new ProductAnalyticsOptions();

        if (options.Enabled &&
            string.Equals(options.Provider, "Postgres", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            services.TryAddSingleton<ProductAnalyticsEventQueue>();
            services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(options.ConnectionString));
            services.AddSingleton<IProductAnalytics, PostgresProductAnalytics>();
            services.AddSingleton<IProductAnalyticsErasure, PostgresProductAnalyticsErasure>();
            services.AddSingleton<IYandexMetrikaIdentityStore, PostgresYandexMetrikaIdentityStore>();
            services.AddSingleton<ProductAnalyticsFlushWorker>();
            services.AddHostedService<ProductAnalyticsFlushWorker>();
        }
        else
        {
            services.AddSingleton<IProductAnalytics, NoOpProductAnalytics>();
            services.AddSingleton<IProductAnalyticsErasure, NoOpProductAnalyticsErasure>();
            services.AddSingleton<IYandexMetrikaIdentityStore, NoOpYandexMetrikaIdentityStore>();
        }

        return services;
    }
}

/// <summary>
/// Validates <see cref="ProductAnalyticsOptions"/> at startup. Salt, host and token are
/// only mandatory when the legacy PostHog sink is enabled — the NoOp and Postgres paths
/// must remain operational in dev/tests where these are intentionally left blank.
/// </summary>
internal sealed class ProductAnalyticsOptionsValidator : IValidateOptions<ProductAnalyticsOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductAnalyticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ProjectToken))
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.IdentitySalt))
        {
            failures.Add(
                $"{ProductAnalyticsOptions.SectionName}:IdentitySalt is required when analytics is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.HostUrl))
        {
            failures.Add(
                $"{ProductAnalyticsOptions.SectionName}:HostUrl is required when analytics is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Environment))
        {
            failures.Add(
                $"{ProductAnalyticsOptions.SectionName}:Environment is required when analytics is enabled.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
