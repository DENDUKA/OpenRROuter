using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.AuthService.Application.Extensions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.BuildingBlocks.Health;

namespace OpenRussiaRouter.AuthService.Infrastructure.Startup;

/// <summary>
/// Auth Service startup configuration.
/// </summary>
public static class Startup
{
    /// <summary>
    /// Configures Auth Service services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc();
        services.AddControllers();
        services.AddHealthChecks();

        var connectionString = configuration.GetConnectionString("AuthDb");
        services.AddAuthInfrastructure(connectionString);
        services.AddAuthApplicationServices();

        return services;
    }

    /// <summary>
    /// Configures the Auth Service application pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.MapGrpcService<GrpcAuthService>();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}