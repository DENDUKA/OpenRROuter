using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BuildingBlocks.Grpc;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;
using OpenRussiaRouter.CollectorService.Application.OpenRouter;
using OpenRussiaRouter.CollectorService.Infrastructure.Grpc;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;


namespace OpenRussiaRouter.CollectorService.Infrastructure.Startup;

public static class Startup
{
    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc();
        services.AddHealthChecks();

        services.AddOptions<CollectorOptions>()
            .Bind(configuration.GetSection("Collector"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CollectorSnapshotStore>();
        services.AddSingleton<NodeCatalogCollector>();
        services.AddSingleton<INodeListProvider, NodeListProvider>();
        services.AddHttpClient<INodeAdminClient, NodeAdminClient>(c => c.Timeout = TimeSpan.FromSeconds(10));

        var grpcCallDeadline = TimeSpan.FromSeconds(
            configuration.GetValue("GrpcServices:CallDeadlineSeconds", 30));
        var controlPlaneUrl = configuration["GrpcServices:ControlPlane"] ?? "http://localhost:5003";
        services.AddGrpcClient<RouterControlPlane.RouterControlPlaneClient>(o => o.Address = new Uri(controlPlaneUrl))
            .ConfigureChannel(o =>
            {
                o.HttpHandler = new SocketsHttpHandler
                {
                    PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                };
            })
            .AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));

        services.AddHostedService<NodePollingWorker>();


        services.AddSingleton<OpenRouterCatalogStore>();
        services.AddOptions<OpenRouterCatalogOptions>()
            .Bind(configuration.GetSection("OpenRouter"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<OpenRouterCatalogClient>(c =>
            c.BaseAddress = new Uri(configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/"));
        services.AddHostedService<OpenRouterCatalogPoller>();

        return services;
    }

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.UseLoggedExceptionHandler("/error");
        app.MapGrpcService<GrpcCollectorService>();
        app.MapHealthChecks("/health");
        return app;
    }
}
