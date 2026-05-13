using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Application.Extensions;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.OpenAi;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;
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

        return services;
    }

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.UseHttpLogging();

        if (!string.Equals(app.Environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            app.UseExceptionHandler("/error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.Use(async (context, next) =>
        {
            var correlationId = CorrelationIdReader.Get(context.Request);
            context.Response.Headers[CorrelationIds.HeaderName] = correlationId;
            context.Response.Headers["X-Request-Id"] = correlationId;
            await next(context);
        });

        app.MapPost("/v1/chat/completions", HandleChatCompletionsAsync);
        app.MapPost("/v1/chat/completions/stream", HandleStreamingChatCompletionsAsync);
        app.MapGet("/v1/models", HandleListModelsAsync);
        app.MapGet("/health", HandleHealthAsync);
        app.MapGet("/ready", HandleReadyAsync);

        return app;
    }

    private static async Task HandleChatCompletionsAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);

        try
        {
            var body = await ChatCompletionRequestReader.ReadBodyAsync(context.Request.Body, context.RequestAborted);
            var metadata = body.Metadata;

            if (string.IsNullOrWhiteSpace(metadata.Model))
            {
                context.Response.StatusCode = 400;
                await WriteJsonErrorAsync(context, "invalid_request_error", ErrorCodes.ValidationFailed, "model is required", requestId);
                return;
            }

            if (!BearerTokenReader.TryRead(context.Request, out var apiKey))
            {
                await WriteJsonErrorAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Bearer token required", requestId);
                return;
            }

            var authenticator = context.RequestServices.GetRequiredService<IApiKeyAuthenticator>();
            var authResult = await authenticator.AuthenticateAsync(apiKey, context.RequestAborted);
            if (!authResult.Succeeded)
            {
                context.Response.StatusCode = 401;
                await WriteJsonErrorAsync(context, "authentication_error", authResult.ErrorCode ?? ErrorCodes.AuthenticationRequired, "Invalid API key", requestId);
                return;
            }

            var billingClient = context.RequestServices.GetRequiredService<IBillingGatewayClient>();
            var balanceResult = await billingClient.CheckBalanceAndModelAsync(authResult.UserId!, metadata.Model, context.RequestAborted);
            if (!balanceResult.Allowed || !string.IsNullOrWhiteSpace(balanceResult.ErrorCode))
            {
                var statusCode = balanceResult.ErrorCode == ErrorCodes.InsufficientBalance ? 402 : 400;
                context.Response.StatusCode = statusCode;
                await WriteJsonErrorAsync(context, "billing_error", balanceResult.ErrorCode ?? ErrorCodes.ValidationFailed, "Balance check failed", requestId);
                return;
            }

            var controlPlane = context.RequestServices.GetRequiredService<IControlPlaneGatewayClient>();
            var lease = await controlPlane.SelectRouterNodeAsync(metadata.Model, requestId, context.RequestAborted);
            if (lease is null)
            {
                await WriteJsonErrorAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "No router available", requestId);
                return;
            }

            var routerProxy = context.RequestServices.GetRequiredService<IRouterProxyClient>();
            var startedAt = DateTimeOffset.UtcNow;
            var response = await routerProxy.SendChatCompletionAsync(lease, body.Json, requestId, context.RequestAborted);

            var completedAt = DateTimeOffset.UtcNow;
            await controlPlane.ReleaseRouterNodeAsync(lease.LeaseId, response.StatusCode >= 500 ? "failed" : "completed", context.RequestAborted);

            RouterUsageExtractor.TryExtract(response.Body, out var usage);
            if (usage.InputTokens > 0 || usage.OutputTokens > 0)
            {
                var usageEvent = new GatewayUsageEvent(
                    requestId,
                    authResult.UserId!,
                    authResult.ApiKeyId!,
                    metadata.Model,
                    lease.NodeId,
                    usage,
                    response.StatusCode >= 500 ? "failed" : "completed",
                    startedAt,
                    completedAt);
                _ = billingClient.RecordUsageAsync(usageEvent, CancellationToken.None);
            }

            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body);
        }
        catch (Exception)
        {
            await WriteJsonErrorAsync(context, "internal_error", ErrorCodes.InternalError, "Internal server error", requestId);
        }
    }

    private static async Task HandleStreamingChatCompletionsAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);

        if (!BearerTokenReader.TryRead(context.Request, out _))
        {
            await WriteJsonErrorAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Bearer token required", requestId);
            return;
        }

        context.Response.ContentType = "text/event-stream";
        await context.Response.WriteAsync("data: [DONE]\n\n");
    }

    private static async Task HandleListModelsAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);

        if (!BearerTokenReader.TryRead(context.Request, out _))
        {
            context.Response.StatusCode = 401;
            await WriteJsonErrorAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Bearer token required", requestId);
            return;
        }

        var billingClient = context.RequestServices.GetRequiredService<IBillingGatewayClient>();
        var models = await billingClient.ListPublicModelsAsync(context.RequestAborted);

        var response = new { @object = "list", data = models.Select(m => new { id = m.Id, @object = "model", created = 0, owned_by = "openrussiarouter" }) };
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, response);
    }

    private static async Task HandleHealthAsync(HttpContext context)
    {
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, new { status = "healthy" });
    }

    private static async Task HandleReadyAsync(HttpContext context)
    {
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, new { status = "ready" });
    }

    private static async Task WriteJsonErrorAsync(HttpContext context, string type, string code, string message, string requestId)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = type switch
        {
            "authentication_error" => 401,
            "billing_error" => 402,
            "invalid_request_error" => 400,
            "service_unavailable" => 503,
            _ => 500
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            error = new
            {
                type,
                code,
                message,
                request_id = requestId
            }
        });
    }
}
