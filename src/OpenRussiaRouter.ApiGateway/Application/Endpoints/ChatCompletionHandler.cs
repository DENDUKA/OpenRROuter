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
    // Safety net for the retry/failover loops: bounds the number of node attempts so a
    // ControlPlane that keeps handing back failing nodes (single node, race, or one that
    // ignores the exclusion set) cannot spin the gateway forever / peg CPU on a request.
    private const int MaxRouterAttempts = 5;

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

            HashSet<string>? excludedNodes = null;
            RouterProxyResponse? lastResponse = null;
            var attempts = 0;

            while (true)
            {
                var auth = await pipeline.AuthorizeAndAcquireLeaseAsync(context, body.Metadata.Model, requestId, excludedNodes, context.RequestAborted);
                
                // If auth is null on the first try, pipeline already wrote the error response.
                // If it's null on a subsequent try, it means no more nodes are available.
                if (auth is null)
                {
                    if (lastResponse != null)
                    {
                        // We exhausted nodes and had a previous error. Return it.
                        context.Response.StatusCode = lastResponse.StatusCode;
                        context.Response.ContentType = lastResponse.ContentType;
                        await context.Response.WriteAsync(lastResponse.Body);
                    }
                    return;
                }

                var patchedJson = SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId);
                var reasoningPatch = ReasoningRequestPatcher.TryNormalize(
                    patchedJson,
                    body.Metadata.Reasoning,
                    auth.SupportsReasoning,
                    auth.AllowedReasoningEfforts ?? []);
                if (!reasoningPatch.IsSuccess)
                {
                    await controlPlaneProvider.ReleaseRouterNodeAsync(auth.Lease.LeaseId, "failed", context.RequestAborted);
                    await GatewayErrors.WriteAsync(
                        context,
                        "invalid_request_error",
                        reasoningPatch.ErrorCode ?? ErrorCodes.ValidationFailed,
                        reasoningPatch.ErrorMessage ?? "Invalid reasoning parameters",
                        requestId);
                    return;
                }

                patchedJson = GeminiSchemaPatcher.SanitizeIfGeminiModel(
                    reasoningPatch.PatchedJson,
                    auth.RouterModelId,
                    body.Metadata.Model ?? "");
                var startedAt = DateTimeOffset.UtcNow;

                var response = await routerProxy.SendChatCompletionAsync(auth.Lease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);
                var completedAt = DateTimeOffset.UtcNow;

                bool isError = response.StatusCode >= 400;

                await controlPlaneProvider.ReleaseRouterNodeAsync(
                    auth.Lease.LeaseId,
                    isError ? "failed" : "completed",
                    context.RequestAborted);

                if (isError)
                {
                    logger.LogWarning("Chat completion downstream error. StatusCode: {StatusCode}, Node: {NodeId}, Body: {Body}", response.StatusCode, auth.Lease.NodeId, response.Body);
                    
                    excludedNodes ??= new HashSet<string>();
                    excludedNodes.Add(auth.Lease.NodeId);
                    lastResponse = response;

                    // Stop after a bounded number of attempts instead of relying solely on the
                    // ControlPlane eventually returning null (see MaxRouterAttempts).
                    if (++attempts >= MaxRouterAttempts)
                    {
                        context.Response.StatusCode = lastResponse.StatusCode;
                        context.Response.ContentType = lastResponse.ContentType;
                        await context.Response.WriteAsync(lastResponse.Body);
                        return;
                    }

                    // Loop again
                    continue;
                }

                var hasUsage = RouterUsageExtractor.TryExtract(response.Body, out var usage) && usage.TotalTokens > 0;
                var status = hasUsage ? "completed" : "usage_missing";

                RecordUsageFireAndForget(requestId, auth, body.Metadata.Model!, response.ProviderName, response.ProviderAccount, usage, status, startedAt, completedAt);

                context.Response.StatusCode = response.StatusCode;
                context.Response.ContentType = response.ContentType;
                await context.Response.WriteAsync(response.Body);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected before the response completed — expected, not an error.
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
            HashSet<string>? excludedNodes = null;
            RouterStreamSession? lastFailedSession = null;
            var attempts = 0;

            while (true)
            {
                var auth = await pipeline.AuthorizeAndAcquireLeaseAsync(context, body.Metadata.Model, requestId, excludedNodes, context.RequestAborted);
                if (auth is null)
                {
                    if (lastFailedSession != null)
                    {
                        await PassThroughErrorAsync(context, lastFailedSession, context.RequestAborted);
                    }
                    return;
                }

                var patchedJson = SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId);
                var reasoningPatch = ReasoningRequestPatcher.TryNormalize(
                    patchedJson,
                    body.Metadata.Reasoning,
                    auth.SupportsReasoning,
                    auth.AllowedReasoningEfforts ?? []);
                if (!reasoningPatch.IsSuccess)
                {
                    await controlPlaneProvider.ReleaseRouterNodeAsync(auth.Lease.LeaseId, "failed", context.RequestAborted);
                    await GatewayErrors.WriteAsync(
                        context,
                        "invalid_request_error",
                        reasoningPatch.ErrorCode ?? ErrorCodes.ValidationFailed,
                        reasoningPatch.ErrorMessage ?? "Invalid reasoning parameters",
                        requestId);
                    return;
                }

                patchedJson = SseRequestPatcher.InjectStreamOptions(
                    GeminiSchemaPatcher.SanitizeIfGeminiModel(
                        reasoningPatch.PatchedJson,
                        auth.RouterModelId,
                        body.Metadata.Model ?? ""));
                var lease = auth.Lease;

                var session = await routerProxy.BeginStreamChatCompletionAsync(lease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);

                if (!session.IsSuccess)
                {
                    var errorBody = await session.ReadErrorBodyAsync(context.RequestAborted);
                    logger.LogWarning("Chat completion stream downstream error. StatusCode: {StatusCode}, Node: {NodeId}, Body: {Body}", session.StatusCode, lease.NodeId, errorBody);

                    await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "failed", context.RequestAborted);
                    
                    excludedNodes ??= new HashSet<string>();
                    excludedNodes.Add(lease.NodeId);

                    if (lastFailedSession != null)
                    {
                        await lastFailedSession.DisposeAsync();
                    }
                    lastFailedSession = session;

                    // Bounded retries (see MaxRouterAttempts) — don't spin forever if the
                    // ControlPlane keeps handing back failing nodes.
                    if (++attempts >= MaxRouterAttempts)
                    {
                        await PassThroughErrorAsync(context, lastFailedSession, context.RequestAborted);
                        return;
                    }

                    continue; // Try next node
                }

                if (lastFailedSession != null)
                {
                    await lastFailedSession.DisposeAsync();
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
                return;
            }
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

    private async Task PassThroughErrorAsync(HttpContext context, RouterStreamSession session, CancellationToken cancellationToken)
    {
        var statusCode = session.StatusCode;
        var body = await session.ReadErrorBodyAsync(cancellationToken);
        
        logger.LogError("Streaming chat completion downstream error. StatusCode: {StatusCode}, Body: {Body}", statusCode, body);
        
        await session.DisposeAsync();

        if (context.Response.HasStarted) return;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(body, cancellationToken);
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
        try
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to submit usage recording. RequestId: {RequestId}", requestId);
        }
    }
}
