using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;

namespace OpenRussiaRouter.ControlPlane.Application.Extensions;

/// <summary>
/// Extension methods for configuring Control Plane dependencies.
/// </summary>
public static class ControlPlaneExtensions
{
    /// <summary>
    /// Adds Control Plane infrastructure dependencies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">PostgreSQL connection string.</param>
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

    /// <summary>
    /// Adds Control Plane application services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddControlPlaneApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<RouterControlPlaneService>();
        services.AddSingleton<IRouterNodeTokenResolver, ConfigurationRouterNodeTokenResolver>();
        services.AddHttpClient<IRouterNodeHealthClient, HttpRouterNodeHealthClient>();
        services.AddHostedService<RouterHealthCheckWorker>();
        services.AddHostedService<RouterLeaseCleanupWorker>();

        return services;
    }
}