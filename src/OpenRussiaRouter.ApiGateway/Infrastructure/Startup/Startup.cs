using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ApiGateway.Application.Extensions;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.OpenAi;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Telemetry;
using OpenRussiaRouter.BuildingBlocks.Usage;

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
            app.UseLoggedExceptionHandler("/error");
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

    private static ILogger GetLogger(HttpContext context, string category)
        => context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(category);

    private sealed record GatewayAuthorizedContext(
        string ApiKey,
        string UserId,
        string ApiKeyId,
        string ApiKeyPrefix,
        string RouterModelId,
        GatewayRouterLease Lease);

    // Общий пайплайн: валидация модели → auth → balance → выбор ноды.
    // Возвращает null и записывает ошибку в ответ если что-то не прошло.
    private static async Task<GatewayAuthorizedContext?> AuthorizeAndAcquireLeaseAsync(
        HttpContext context,
        string? model,
        string requestId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            await WriteJsonErrorAsync(context, "invalid_request_error", ErrorCodes.ValidationFailed, "model is required", requestId);
            return null;
        }

        if (!BearerTokenReader.TryRead(context.Request, out var apiKey))
        {
            await WriteJsonErrorAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Bearer token required", requestId);
            return null;
        }

        var authenticator = context.RequestServices.GetRequiredService<IApiKeyAuthenticator>();
        var authResult = await authenticator.AuthenticateAsync(apiKey, cancellationToken);
        if (!authResult.Succeeded)
        {
            await WriteJsonErrorAsync(context, "authentication_error", authResult.ErrorCode ?? ErrorCodes.AuthenticationRequired, "Invalid API key", requestId);
            return null;
        }

        var billingClient = context.RequestServices.GetRequiredService<IBillingGatewayClient>();
        var balanceResult = await billingClient.CheckBalanceAndModelAsync(authResult.UserId!, model, cancellationToken);
        if (!balanceResult.Allowed || !string.IsNullOrWhiteSpace(balanceResult.ErrorCode))
        {
            var errorType = balanceResult.ErrorCode == ErrorCodes.InsufficientBalance
                ? "billing_error"
                : "invalid_request_error";
            await WriteJsonErrorAsync(context, errorType, balanceResult.ErrorCode ?? ErrorCodes.ValidationFailed, "Balance check failed", requestId);
            return null;
        }

        var controlPlane = context.RequestServices.GetRequiredService<IControlPlaneGatewayClient>();
        var lease = await controlPlane.SelectRouterNodeAsync(model, requestId, cancellationToken);
        if (lease is null)
        {
            await WriteJsonErrorAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "No router available", requestId);
            return null;
        }

        return new GatewayAuthorizedContext(
            apiKey,
            authResult.UserId!,
            authResult.ApiKeyId!,
            authResult.ApiKeyPrefix!,
            balanceResult.RouterModelId,
            lease);
    }

    private static async Task HandleChatCompletionsAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);

        try
        {
            var body = await ChatCompletionRequestReader.ReadBodyAsync(context.Request.Body, context.RequestAborted);

            if (body.Metadata.Stream)
            {
                await ExecuteStreamingAsync(context, body, requestId);
                return;
            }

            var auth = await AuthorizeAndAcquireLeaseAsync(context, body.Metadata.Model, requestId, context.RequestAborted);
            if (auth is null) return;

            var patchedJson = SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId);
            var routerProxy = context.RequestServices.GetRequiredService<IRouterProxyClient>();
            var controlPlane = context.RequestServices.GetRequiredService<IControlPlaneGatewayClient>();
            var billingClient = context.RequestServices.GetRequiredService<IBillingGatewayClient>();

            var startedAt = DateTimeOffset.UtcNow;
            var response = await routerProxy.SendChatCompletionAsync(auth.Lease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);
            var completedAt = DateTimeOffset.UtcNow;

            await controlPlane.ReleaseRouterNodeAsync(auth.Lease.LeaseId, response.StatusCode >= 500 ? "failed" : "completed", context.RequestAborted);

            var hasUsage = RouterUsageExtractor.TryExtract(response.Body, out var usage) && usage.TotalTokens > 0;
            var status = response.StatusCode >= 500 ? "failed" : hasUsage ? "completed" : "usage_missing";

            _ = billingClient.RecordUsageAsync(
                new GatewayUsageEvent(requestId, auth.UserId, auth.ApiKeyId, auth.ApiKeyPrefix,
                    body.Metadata.Model!, auth.Lease.NodeId, usage, status, startedAt, completedAt),
                CancellationToken.None)
                .ContinueWith(
                    t => GetLogger(context, "OpenRussiaRouter.ApiGateway.Billing")
                        .LogError(t.Exception, "Usage recording failed. RequestId: {RequestId}", requestId),
                    TaskContinuationOptions.OnlyOnFaulted);

            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body);
        }
        catch (Exception ex)
        {
            GetLogger(context, "OpenRussiaRouter.ApiGateway.ChatCompletions")
                .LogWarning(ex, "Chat completion handling failed. RequestId: {RequestId}", requestId);
            await WriteJsonErrorAsync(context, "internal_error", ErrorCodes.InternalError, "Internal server error", requestId);
        }
    }

    private static async Task HandleStreamingChatCompletionsAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);
        var body = await ChatCompletionRequestReader.ReadBodyAsync(context.Request.Body, context.RequestAborted);
        await ExecuteStreamingAsync(context, body, requestId);
    }

    // GATEWAY-003 + GATEWAY-004: full SSE streaming with one retry before first chunk
    private static async Task ExecuteStreamingAsync(HttpContext context, ChatCompletionRequestBody body, string requestId)
    {
        try
        {
            var auth = await AuthorizeAndAcquireLeaseAsync(context, body.Metadata.Model, requestId, context.RequestAborted);
            if (auth is null) return;

            var patchedJson = SseRequestPatcher.InjectStreamOptions(
                SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId));
            var controlPlane = context.RequestServices.GetRequiredService<IControlPlaneGatewayClient>();
            var billingClient = context.RequestServices.GetRequiredService<IBillingGatewayClient>();
            var routerProxy = context.RequestServices.GetRequiredService<IRouterProxyClient>();
            var lease = auth.Lease;

            var session = await routerProxy.BeginStreamChatCompletionAsync(lease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);

            // GATEWAY-004: single retry on a different node before any bytes reach the client
            if (!session.IsSuccess)
            {
                await session.DisposeAsync();
                await controlPlane.ReleaseRouterNodeAsync(lease.LeaseId, "failed", context.RequestAborted);

                var fallbackLease = await controlPlane.SelectRouterNodeAsync(body.Metadata.Model!, requestId, context.RequestAborted);
                if (fallbackLease is null)
                {
                    await WriteJsonErrorAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "No router available", requestId);
                    return;
                }

                session = await routerProxy.BeginStreamChatCompletionAsync(fallbackLease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);
                lease = fallbackLease;

                if (!session.IsSuccess)
                {
                    await session.DisposeAsync();
                    await controlPlane.ReleaseRouterNodeAsync(lease.LeaseId, "failed", context.RequestAborted);
                    await WriteJsonErrorAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "Router nodes unavailable", requestId);
                    return;
                }
            }

            // Commit SSE response — no error returns possible after this point
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            var startedAt = DateTimeOffset.UtcNow;
            TokenUsage usage;
            await using (session)
            {
                usage = await session.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            var completedAt = DateTimeOffset.UtcNow;

            await controlPlane.ReleaseRouterNodeAsync(lease.LeaseId, "completed", CancellationToken.None);

            var status = usage.TotalTokens > 0 ? "completed" : "usage_missing";
            _ = billingClient.RecordUsageAsync(
                new GatewayUsageEvent(requestId, auth.UserId, auth.ApiKeyId, auth.ApiKeyPrefix,
                    body.Metadata.Model!, lease.NodeId, usage, status, startedAt, completedAt),
                CancellationToken.None)
                .ContinueWith(
                    t => GetLogger(context, "OpenRussiaRouter.ApiGateway.Billing")
                        .LogError(t.Exception, "Usage recording failed. RequestId: {RequestId}", requestId),
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal for SSE, nothing to do
        }
        catch (Exception ex)
        {
            GetLogger(context, "OpenRussiaRouter.ApiGateway.ChatCompletionsStream")
                .LogWarning(ex, "Streaming chat completion failed. RequestId: {RequestId}", requestId);

            if (!context.Response.HasStarted)
                await WriteJsonErrorAsync(context, "internal_error", ErrorCodes.InternalError, "Internal server error", requestId);
        }
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
