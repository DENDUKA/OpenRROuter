using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.AuthService.Application.Extensions;
using OpenRussiaRouter.AuthService.Infrastructure.Grpc;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.BuildingBlocks.Health;
using OpenRussiaRouter.BuildingBlocks.Logging;

namespace OpenRussiaRouter.AuthService.Infrastructure.Startup;

public static class Startup
{

    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc();
        services.AddControllers();
        services.AddHealthChecks();

        var connectionString = configuration.GetConnectionString("AuthDb");
        services.AddAuthInfrastructure(connectionString);
        services.AddAuthApplicationServices();
        services.AddProductAnalytics(configuration);

        return services;
    }

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.UseLoggedExceptionHandler("/error");
        app.MapGrpcService<GrpcAuthService>();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}
