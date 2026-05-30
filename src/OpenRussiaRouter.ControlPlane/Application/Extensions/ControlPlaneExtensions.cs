using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ControlPlane.Application.Metrics;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Infrastructure.Http;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;
using Quartz;

namespace OpenRussiaRouter.ControlPlane.Application.Extensions;

public static class ControlPlaneExtensions
{

    public static IServiceCollection AddControlPlaneInfrastructure(
        this IServiceCollection services,
        string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IControlPlaneDbConnectionFactory>(_ => new ControlPlaneDbConnectionFactory(connectionString));
            services.AddSingleton<IControlPlaneRepository, ControlPlaneRepository>();
        }
        else
        {
            services.AddSingleton<IControlPlaneRepository, InMemoryControlPlaneRepository>();
        }

        return services;
    }

    public static IServiceCollection AddControlPlaneApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<NodeModelCapabilityStore>();
        services.AddSingleton<RouterControlPlaneService>();
        services.AddHttpClient<IRouterNodeHealthProvider, RouterNodeHealthProvider>();
        services.AddSingleton<RouterNodeProviderCacheStore>();
        services.AddHttpClient<RouterNodeProvider>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddTransient<IRouterNodeProvider, CachedRouterNodeProvider>();
        services.AddHostedService<RouterHealthCheckWorker>();
        services.AddHostedService<RouterLeaseCleanupWorker>();

        services.AddHttpClient<ProviderQuotaCollectorWorker>();
        services.AddOptions<ProviderQuotaCollectorOptions>()
            .Bind(configuration.GetSection("ProviderQuotaCollector"))
            .ValidateDataAnnotations();
        services.AddHostedService<ProviderQuotaCollectorWorker>();

        services.AddOptions<RouterModelCapabilityOptions>()
            .Bind(configuration.GetSection("RouterModelCapability"))
            .ValidateDataAnnotations();

        services.AddQuartz(q =>
        {
            q.UseInMemoryStore();
            var jobKey = new JobKey(nameof(RouterModelCapabilityJob));
            q.AddJob<RouterModelCapabilityJob>(opts => opts.WithIdentity(jobKey));
            q.AddTrigger(opts =>
            {
                var interval = configuration
                    .GetSection("RouterModelCapability")
                    .GetValue("Interval", TimeSpan.FromMinutes(5));
                opts.ForJob(jobKey)
                    .WithIdentity("RouterModelCapabilityTrigger")
                    .StartNow()
                    .WithSimpleSchedule(s => s
                        .WithInterval(interval)
                        .RepeatForever());
            });
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }

}
