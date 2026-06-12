using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ControlPlane.Application.Extensions;
using OpenRussiaRouter.ControlPlane.Application.Options;
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
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<RouterNodeProviderCacheOptions>()
            .Bind(configuration.GetSection("RouterNodeProviderCache"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<RouterLeaseCleanupOptions>()
            .Bind(configuration.GetSection("RouterLeaseCleanup"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CapabilityReplicaOptions>()
            .Bind(configuration.GetSection("CapabilityReplica"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var grpcCallDeadline = TimeSpan.FromSeconds(
            configuration.GetValue("GrpcServices:CallDeadlineSeconds", 30));

        var authUrl = configuration["GrpcServices:Auth"] ?? "http://localhost:5001";
        services.AddGrpcClient<OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService.AuthServiceClient>(
                o => o.Address = new Uri(authUrl))
            .ConfigureChannel(o =>
            {
                o.HttpHandler = new SocketsHttpHandler
                {
                    PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                };
            })
            .AddInterceptor(() => new OpenRussiaRouter.BuildingBlocks.Grpc.GrpcDeadlineInterceptor(grpcCallDeadline));

        var collectorUrl = configuration["GrpcServices:Collector"] ?? "http://localhost:5005";
        services.AddGrpcClient<OpenRussiaRouter.Contracts.Grpc.Collector.V1.Collector.CollectorClient>(
                o => o.Address = new Uri(collectorUrl))
            .ConfigureChannel(o =>
            {
                o.HttpHandler = new SocketsHttpHandler
                {
                    PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                };
            })
            .AddInterceptor(() => new OpenRussiaRouter.BuildingBlocks.Grpc.GrpcDeadlineInterceptor(grpcCallDeadline));

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
