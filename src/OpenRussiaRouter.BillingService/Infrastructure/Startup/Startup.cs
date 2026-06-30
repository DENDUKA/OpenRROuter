using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BillingService.Application.Extensions;
using OpenRussiaRouter.BillingService.Infrastructure.Auth;
using OpenRussiaRouter.BillingService.Infrastructure.Grpc;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Infrastructure.Workers;

namespace OpenRussiaRouter.BillingService.Infrastructure.Startup;

public static class Startup
{

    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc(options =>
        {
            // TS-003: guard every mutating BillingService gRPC method with s2s auth.
            options.Interceptors.Add<ServiceAuthInterceptor>();
        });
        services.AddControllers();
        services.AddHealthChecks();

        var connectionString = configuration.GetConnectionString("BillingDb");
        services.AddBillingInfrastructure(connectionString);
        services.AddBillingApplicationServices();
        services.AddProductAnalytics(configuration);
        services.Configure<ExchangeRateOptions>(configuration.GetSection("ExchangeRates"));
        services.Configure<PaymentOptions>(configuration.GetSection("Payments"));
        services.Configure<ServiceAuthOptions>(configuration.GetSection("ServiceAuth"));
        services.AddHttpClient("BillingFx");
        services.AddSingleton<FxQuoteService>();
        services.AddHostedService<FxRateRefresher>();
        services.AddHostedService<PaymentEventRecoveryWorker>();

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
