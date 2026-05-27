using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ControlPlane.Application.Extensions;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Infrastructure.Grpc;
using OpenRussiaRouter.BuildingBlocks.Logging;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Startup;

public static class Startup
{

    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc();
        services.AddControllers();
        services.AddHealthChecks();

        services.AddOptions<RouterHealthCheckOptions>()
            .Bind(configuration.GetSection("RouterHealthChecks"))
            .ValidateDataAnnotations();
        services.AddOptions<RouterNodeProviderCacheOptions>()
            .Bind(configuration.GetSection("RouterNodeProviderCache"))
            .ValidateDataAnnotations();
        services.AddOptions<RouterLeaseCleanupOptions>()
            .Bind(configuration.GetSection("RouterLeaseCleanup"))
            .ValidateDataAnnotations();

        var connectionString = configuration.GetConnectionString("ControlPlaneDb");
        services.AddControlPlaneInfrastructure(connectionString);
        services.AddControlPlaneApplicationServices(configuration);

        return services;
    }

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.UseLoggedExceptionHandler("/error");
        app.MapGrpcService<GrpcRouterControlPlaneService>();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}
