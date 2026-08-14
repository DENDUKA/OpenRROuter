using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Application.Extensions;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Infrastructure.Startup;

public static class Startup
{
    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();
        services.AddHttpLogging(_ => { });
        services.AddHealthChecks();

        services.AddGatewayGrpcClients(configuration);
        services.AddGatewayRouter();
        services.AddGatewayHandlers(configuration);
        services.AddGatewayUsageOutbox(configuration);
        services.AddGatewayRateLimiting(configuration);

        return services;
    }

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.UseHttpLogging();

        if (!string.Equals(app.Environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            app.UseLoggedExceptionHandler("/error");
            app.UseHsts();
        }

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            await next(context);
        });

        app.Use(async (context, next) =>
        {
            var correlationId = CorrelationIdReader.Get(context.Request);
            context.Response.Headers[CorrelationIds.HeaderName] = correlationId;
            context.Response.Headers["X-Request-Id"] = correlationId;
            await next(context);
        });

        app.UseMiddleware<VisitorMetricsMiddleware>();
        app.UseRateLimiter();

        app.MapGatewayEndpoints();

        return app;
    }
}
