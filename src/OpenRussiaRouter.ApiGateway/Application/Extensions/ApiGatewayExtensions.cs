using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Telemetry;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.ApiGateway.Application.Extensions;

/// <summary>
/// Extension methods for configuring API Gateway dependencies.
/// </summary>
public static class ApiGatewayExtensions
{
    /// <summary>
    /// Adds API Gateway gRPC client dependencies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddGatewayGrpcClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GatewayGrpcOptions>(options =>
        {
            options.Auth = configuration["GrpcServices:Auth"] ?? "http://localhost:5001";
            options.Billing = configuration["GrpcServices:Billing"] ?? "http://localhost:5002";
            options.ControlPlane = configuration["GrpcServices:ControlPlane"] ?? "http://localhost:5003";
        });
        services.Configure<GatewayHttpClientOptions>(options =>
        {
            options.PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan;
            options.KeepAlivePingDelay = TimeSpan.FromSeconds(60);
            options.KeepAlivePingTimeout = TimeSpan.FromSeconds(30);
        });

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
        services.AddScoped<IApiKeyAuthenticator, GrpcApiKeyAuthenticator>();

        var billingGrpcUrl = configuration["GrpcServices:Billing"] ?? "http://localhost:5002";
        services.AddGrpcClient<BillingGrpc.BillingServiceClient>(options =>
        {
            options.Address = new Uri(billingGrpcUrl);
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
        services.AddScoped<IBillingGatewayClient, GrpcBillingGatewayClient>();

        var controlPlaneGrpcUrl = configuration["GrpcServices:ControlPlane"] ?? "http://localhost:5003";
        services.AddGrpcClient<ControlGrpc.RouterControlPlaneClient>(options =>
        {
            options.Address = new Uri(controlPlaneGrpcUrl);
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
        services.AddScoped<IControlPlaneGatewayClient, GrpcControlPlaneGatewayClient>();

        return services;
    }

    /// <summary>
    /// Adds API Gateway router dependencies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddGatewayRouter(this IServiceCollection services)
    {
        services.AddReverseProxy();
        services.AddSingleton<IRouterNodeTokenProvider, ConfigurationRouterNodeTokenProvider>();

        services.AddScoped<IRouterProxyClient, YarpRouterProxyClient>();

        return services;
    }
}
