using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ControlPlane.Application;
using OpenRussiaRouter.ControlPlane.Application.Extensions;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Startup;

/// <summary>
/// Control Plane startup configuration.
/// </summary>
public static class Startup
{
    /// <summary>
    /// Configures Control Plane services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc();
        services.AddControllers();
        services.AddHealthChecks();

        services.AddOptions<RouterHealthCheckOptions>()
            .Bind(configuration.GetSection("RouterHealthChecks"))
            .ValidateDataAnnotations();
        services.AddOptions<RouterLeaseCleanupOptions>()
            .Bind(configuration.GetSection("RouterLeaseCleanup"))
            .ValidateDataAnnotations();

        var connectionString = configuration.GetConnectionString("ControlPlaneDb");
        services.AddControlPlaneInfrastructure(connectionString);
        services.AddControlPlaneApplicationServices();

        return services;
    }

    /// <summary>
    /// Configures the Control Plane application pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.MapGrpcService<GrpcRouterControlPlaneService>();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}