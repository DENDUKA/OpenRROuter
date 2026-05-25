using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Infrastructure.Http;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;

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

    public static IServiceCollection AddControlPlaneApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<RouterControlPlaneService>();
        services.AddHttpClient<IRouterNodeHealthProvider, RouterNodeHealthProvider>();
        services.AddSingleton<RouterNodeProviderCacheStore>();
        services.AddHttpClient<RouterNodeProvider>();
        services.AddTransient<IRouterNodeProvider, CachedRouterNodeProvider>();
        services.AddHostedService<RouterHealthCheckWorker>();
        services.AddHostedService<RouterLeaseCleanupWorker>();

        return services;
    }

}
