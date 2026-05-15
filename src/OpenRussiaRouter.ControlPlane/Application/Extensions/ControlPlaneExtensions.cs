using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Infrastructure;
using OpenRussiaRouter.ControlPlane.Infrastructure.Background;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.ControlPlane.Application.Extensions;

public static class ControlPlaneExtensions
{

    public static IServiceCollection AddControlPlaneInfrastructure(
        this IServiceCollection services,
        string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IControlPlaneDbConnectionFactory>(_ => new ControlPlaneDbConnectionFactory(connectionString));
            services.AddSingleton<IControlPlaneRepository, ControlPlaneRepository>();
        }
        else
        {
            services.AddSingleton<IControlPlaneRepository, InMemoryControlPlaneRepository>();
        }

        return services;
    }

    public static IServiceCollection AddControlPlaneApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<RouterControlPlaneService>();
        services.AddSingleton<IRouterNodeTokenResolver, ConfigurationRouterNodeTokenResolver>();
        services.AddHttpClient<IRouterNodeHealthClient, HttpRouterNodeHealthClient>();
        services.AddHostedService<RouterHealthCheckWorker>();
        services.AddHostedService<RouterLeaseCleanupWorker>();

        return services;
    }

    public static IServiceCollection AddControlPlaneKeySyncServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authGrpcUrl = configuration["GrpcServices:Auth"] ?? "http://localhost:5001";
        services.AddGrpcClient<AuthGrpc.AuthServiceClient>(options =>
        {
            options.Address = new Uri(authGrpcUrl);
        })
        .ConfigureChannel(options =>
        {
            options.HttpHandler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            };
        });

        services.AddHttpClient<RouterNodeKeySyncClient>();
        services.AddScoped<IRouterKeySyncService, RouterKeySyncService>();
        services.AddHostedService<KeySyncWorker>();

        return services;
    }
}