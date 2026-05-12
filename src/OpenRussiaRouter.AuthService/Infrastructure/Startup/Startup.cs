using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.AuthService.Application.Extensions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.BuildingBlocks.Health;

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

        return services;
    }

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.MapGrpcService<GrpcAuthService>();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}