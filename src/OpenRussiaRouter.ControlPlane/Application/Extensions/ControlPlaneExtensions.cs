using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Infrastructure.Http;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;
using OpenRussiaRouter.ControlPlane.Infrastructure.Workers;

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
        services.AddSingleton<RouterNodeSnapshotStore>();
        services.AddSingleton<RouterControlPlaneService>();
        services.AddHttpClient<IRouterNodeHealthProvider, RouterNodeHealthProvider>();
        services.AddSingleton<RouterNodeProviderCacheStore>();
        services.AddHttpClient<RouterNodeProvider>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddTransient<IRouterNodeProvider, CachedRouterNodeProvider>();
        services.AddHostedService<RouterHealthCheckWorker>();
        services.AddHostedService<RouterLeaseCleanupWorker>();
        services.AddHostedService<RouterNodeSnapshotRefresher>();
        services.AddHostedService<CapabilityReplicaRefresher>();

        return services;
    }

}
