using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BillingService.Application.Extensions;
using OpenRussiaRouter.BillingService.Application.Services;

namespace OpenRussiaRouter.BillingService.Infrastructure.Startup;

/// <summary>
/// Billing Service startup configuration.
/// </summary>
public static class Startup
{
    /// <summary>
    /// Configures Billing Service services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc();
        services.AddControllers();
        services.AddHealthChecks();

        var connectionString = configuration.GetConnectionString("BillingDb");
        services.AddBillingInfrastructure(connectionString);
        services.AddBillingApplicationServices();

        return services;
    }

    /// <summary>
    /// Configures the Billing Service application pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.MapGrpcService<GrpcBillingService>();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}