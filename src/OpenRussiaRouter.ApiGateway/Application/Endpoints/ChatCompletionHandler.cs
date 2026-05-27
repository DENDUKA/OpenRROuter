using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ApiGateway.Application.Services;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.OpenAi;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Telemetry;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Application.Endpoints;

/// <summary>
/// Обработчики POST /v1/chat/completions и POST /v1/chat/completions/stream.
/// Нестриминговый путь: YARP proxy. Стриминговый: ResponseHeadersRead + одна ретрай-нода.
/// </summary>
public sealed class ChatCompletionHandler(
    IGatewayPipeline pipeline,
    IBillingProvider billingProvider,
    IControlPlaneProvider controlPlaneProvider,
    IRouterProxyProvider routerProxy,
    ILogger<ChatCompletionHandler> logger)
{
    public async Task HandleAsync(HttpContext context)
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

            var auth = await pipeline.AuthorizeAndAcquireLeaseAsync(context, body.Metadata.Model, requestId, context.RequestAborted);
            if (auth is null) return;

            var patchedJson = SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId);
            var startedAt = DateTimeOffset.UtcNow;

            var response = await routerProxy.SendChatCompletionAsync(auth.Lease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);
            var completedAt = DateTimeOffset.UtcNow;

            await controlPlaneProvider.ReleaseRouterNodeAsync(
                auth.Lease.LeaseId,
                response.StatusCode >= 500 ? "failed" : "completed",
                context.RequestAborted);

            var hasUsage = RouterUsageExtractor.TryExtract(response.Body, out var usage) && usage.TotalTokens > 0;
            var status = response.StatusCode >= 500 ? "failed" : hasUsage ? "completed" : "usage_missing";

            RecordUsageFireAndForget(requestId, auth, body.Metadata.Model!, response.ProviderName, response.ProviderAccount, usage, status, startedAt, completedAt);

            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Chat completion handling failed. RequestId: {RequestId}", requestId);
            await GatewayErrors.WriteAsync(context, "internal_error", ErrorCodes.InternalError, "Internal server error", requestId);
        }
    }

    public async Task HandleStreamAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);
        var body = await ChatCompletionRequestReader.ReadBodyAsync(context.Request.Body, context.RequestAborted);
        await ExecuteStreamingAsync(context, body, requestId);
    }

    // GATEWAY-003 + GATEWAY-004: full SSE streaming with one retry before first chunk
    private async Task ExecuteStreamingAsync(HttpContext context, ChatCompletionRequestBody body, string requestId)
    {
        try
        {
            var auth = await pipeline.AuthorizeAndAcquireLeaseAsync(context, body.Metadata.Model, requestId, context.RequestAborted);
            if (auth is null) return;

            var patchedJson = SseRequestPatcher.InjectStreamOptions(
                SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId));
            var lease = auth.Lease;

            var session = await routerProxy.BeginStreamChatCompletionAsync(lease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);

            // GATEWAY-004: single retry on a different node before any bytes reach the client
            if (!session.IsSuccess)
            {
                await session.DisposeAsync();
                await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "failed", context.RequestAborted);

                var fallbackLease = await controlPlaneProvider.SelectRouterNodeAsync(body.Metadata.Model!, requestId, context.RequestAborted);
                if (fallbackLease is null)
                {
                    await GatewayErrors.WriteAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "No router available", requestId);
                    return;
                }

                session = await routerProxy.BeginStreamChatCompletionAsync(fallbackLease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);
                lease = fallbackLease;

                if (!session.IsSuccess)
                {
                    await session.DisposeAsync();
                    await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "failed", context.RequestAborted);
                    await GatewayErrors.WriteAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "Router nodes unavailable", requestId);
                    return;
                }
            }

            // Commit SSE response — no error returns possible after this point
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            var startedAt = DateTimeOffset.UtcNow;
            TokenUsage usage;
            string? providerName;
            string? providerAccount;

            await using (session)
            {
                providerName = session.ProviderName;
                providerAccount = session.ProviderAccount;
                usage = await session.CopyToAsync(context.Response.Body, context.RequestAborted);
            }

            var completedAt = DateTimeOffset.UtcNow;
            await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "completed", CancellationToken.None);

            var status = usage.TotalTokens > 0 ? "completed" : "usage_missing";
            RecordUsageFireAndForget(requestId, auth, body.Metadata.Model!, providerName, providerAccount, usage, status, startedAt, completedAt);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal for SSE, nothing to do
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Streaming chat completion failed. RequestId: {RequestId}", requestId);
            if (!context.Response.HasStarted)
                await GatewayErrors.WriteAsync(context, "internal_error", ErrorCodes.InternalError, "Internal server error", requestId);
        }
    }

    private void RecordUsageFireAndForget(
        string requestId,
        GatewayAuthorizedContext auth,
        string modelId,
        string? providerName,
        string? providerAccount,
        TokenUsage usage,
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        _ = billingProvider.RecordUsageAsync(
            new GatewayUsageEvent(requestId, auth.UserId, auth.ApiKeyId, auth.ApiKeyPrefix,
                modelId, auth.Lease.NodeId, usage, status, startedAt, completedAt,
                providerName, providerAccount),
            CancellationToken.None)
            .ContinueWith(
                t => logger.LogError(t.Exception, "Usage recording failed. RequestId: {RequestId}", requestId),
                TaskContinuationOptions.OnlyOnFaulted);
    }
}
