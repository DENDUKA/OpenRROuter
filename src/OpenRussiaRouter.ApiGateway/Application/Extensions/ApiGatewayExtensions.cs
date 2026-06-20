using Grpc.Core;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
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
        services.AddOptions<GatewayGrpcOptions>()
            .Configure(options =>
            {
                options.Auth = configuration["GrpcServices:Auth"] ?? "http://localhost:5001";
                options.Billing = configuration["GrpcServices:Billing"] ?? "http://localhost:5002";
                options.ControlPlane = configuration["GrpcServices:ControlPlane"] ?? "http://localhost:5003";
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.Configure<GatewayHttpClientOptions>(options =>
        {
            options.PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan;
            options.KeepAlivePingDelay = TimeSpan.FromSeconds(60);
            options.KeepAlivePingTimeout = TimeSpan.FromSeconds(30);
        });

        // Default per-call deadline for all gRPC clients (overridable via GrpcServices:CallDeadlineSeconds).
        var grpcCallDeadline = TimeSpan.FromSeconds(configuration.GetValue("GrpcServices:CallDeadlineSeconds", 30));

        // TS-003: s2s credentials for outbound BillingService calls.
        var billingS2sCaller = configuration["ServiceAuth:BillingCaller"] ?? "apigateway";
        var billingS2sToken = configuration["ServiceAuth:BillingToken"];
        if (string.IsNullOrWhiteSpace(billingS2sToken))
        {
            throw new InvalidOperationException(
                "ServiceAuth:BillingToken is required for ApiGateway. Configure the shared s2s token in appsettings or a secret store.");
        }

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
        .AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline))
        .AddInterceptor(() => new ServiceAuthClientInterceptor(billingS2sCaller, billingS2sToken))
        .AddResilienceHandler("billing-usage", pipeline =>
        {
            var sp = services.BuildServiceProvider();
            var retryLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("BillingUsageRetry");

            pipeline
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 6,
                    Delay = TimeSpan.FromSeconds(4),
                    BackoffType = DelayBackoffType.Exponential,
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<RpcException>()
                        .Handle<HttpRequestException>(),
                    Name = "billing-retry",
                    OnRetry = args =>
                    {
                        retryLogger.LogWarning(args.Outcome.Exception,
                            "Billing usage retry — transisent failure.");
                        return ValueTask.CompletedTask;
                    }
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    FailureRatio = 0.5,
                    MinimumThroughput = 10,
                    BreakDuration = TimeSpan.FromSeconds(15),
                    Name = "billing-circuit-breaker"
                });
        });
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

    public static IServiceCollection AddGatewayHandlers(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GatewayRetryOptions>(configuration.GetSection("GatewayRetry"));
        services.AddScoped<IGatewayPipeline, GatewayPipeline>();
        services.AddScoped<ChatCompletionHandler>();
        services.AddScoped<ImageGenerationHandler>();
        services.AddScoped<TextToSpeechHandler>();
        services.AddScoped<ModelsHandler>();

        return services;
    }

    public static WebApplication MapGatewayEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/chat/completions",
            (ChatCompletionHandler handler, HttpContext ctx) => handler.HandleAsync(ctx))
            .RequireRateLimiting(GatewayRateLimiting.PolicyName);
        app.MapPost("/v1/chat/completions/stream",
            (ChatCompletionHandler handler, HttpContext ctx) => handler.HandleStreamAsync(ctx))
            .RequireRateLimiting(GatewayRateLimiting.PolicyName);
        app.MapPost("/v1/images/generations",
            (ImageGenerationHandler handler, HttpContext ctx) => handler.HandleAsync(ctx))
            .RequireRateLimiting(GatewayRateLimiting.PolicyName);
        app.MapPost("/v1/audio/speech",
            (TextToSpeechHandler handler, HttpContext ctx) => handler.HandleAsync(ctx))
            .RequireRateLimiting(GatewayRateLimiting.PolicyName);
        app.MapGet("/v1/models",
            (ModelsHandler handler, HttpContext ctx) => handler.HandleAsync(ctx));
        app.MapGet("/health",
            () => Results.Json(new { status = "healthy" }));
        app.MapGet("/ready",
            () => Results.Json(new { status = "ready" }));

        return app;
    }
}
