using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Application.Endpoints;
using OpenRussiaRouter.ApiGateway.Application.Services;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Grpc;
using OpenRussiaRouter.BuildingBlocks.Telemetry;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

namespace OpenRussiaRouter.ApiGateway.Application.Extensions;

public static class ApiGatewayExtensions
{

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

        // Default per-call deadline for all gRPC clients (overridable via GrpcServices:CallDeadlineSeconds).
        var grpcCallDeadline = TimeSpan.FromSeconds(configuration.GetValue("GrpcServices:CallDeadlineSeconds", 30));

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
        })
        .AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));
        services.AddScoped<IApiKeyProvider, GrpcApiKeyProvider>();

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
        })
        .AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));
        services.AddScoped<IBillingProvider, GrpcBillingProvider>();

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
        })
        .AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));
        services.AddScoped<IControlPlaneProvider, GrpcControlPlaneProvider>();

        return services;
    }

    public static IServiceCollection AddGatewayRouter(this IServiceCollection services)
    {
        services.AddReverseProxy();
        services.AddScoped<IRouterProxyProvider, YarpRouterProxyProvider>();

        return services;
    }

    public static IServiceCollection AddGatewayHandlers(this IServiceCollection services)
    {
        services.AddScoped<IGatewayPipeline, GatewayPipeline>();
        services.AddScoped<ChatCompletionHandler>();
        services.AddScoped<ModelsHandler>();

        return services;
    }

    public static WebApplication MapGatewayEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/chat/completions",
            (ChatCompletionHandler handler, HttpContext ctx) => handler.HandleAsync(ctx));
        app.MapPost("/v1/chat/completions/stream",
            (ChatCompletionHandler handler, HttpContext ctx) => handler.HandleStreamAsync(ctx));
        app.MapGet("/v1/models",
            (ModelsHandler handler, HttpContext ctx) => handler.HandleAsync(ctx));
        app.MapGet("/health",
            () => Results.Json(new { status = "healthy" }));
        app.MapGet("/ready",
            () => Results.Json(new { status = "ready" }));

        return app;
    }
}
