using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BillingService.Application.Extensions;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BuildingBlocks.Logging;

namespace OpenRussiaRouter.BillingService.Infrastructure.Startup;

public static class Startup
{

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

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.UseLoggedExceptionHandler("/error");
        app.MapGrpcService<GrpcBillingService>();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}
