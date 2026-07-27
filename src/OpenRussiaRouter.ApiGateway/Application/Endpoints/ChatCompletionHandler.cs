using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ApiGateway.Application.Extensions;
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
/// Нестриминговый путь: RouterRetryExecutor (общий с image/TTS).
/// Стриминговый: ResponseHeadersRead + ретрай до первого успешного чанка.
/// </summary>
public sealed class ChatCompletionHandler(
    RouterRetryExecutor retryExecutor,
    IGatewayPipeline pipeline,
    IUsageOutboxRepository outboxRepository,
    IControlPlaneProvider controlPlaneProvider,
    IRouterProxyProvider routerProxy,
    IOptions<GatewayRetryOptions> retryOptions,
    ILogger<ChatCompletionHandler> logger)
{
    private int MaxRouterAttempts => retryOptions.Value.MaxRouterAttempts;

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

            await retryExecutor.ExecuteAsync(
                context,
                body.Metadata.Model ?? "",
                requestId,
                patchBody: auth =>
                {
                    var patchedJson = SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId);

                    var reasoningPatch = ReasoningRequestPatcher.TryNormalize(
                        patchedJson,
                        body.Metadata.Reasoning,
                        auth.SupportsReasoning,
                        auth.AllowedReasoningEfforts ?? [],
                        auth.DefaultReasoningEffort);

                    if (!reasoningPatch.IsSuccess)
                        throw new RoutingValidationException(
                            reasoningPatch.ErrorMessage ?? "Invalid reasoning parameters",
                            reasoningPatch.ErrorCode ?? ErrorCodes.ValidationFailed);

                    return GeminiSchemaPatcher.SanitizeIfGeminiModel(
                        reasoningPatch.PatchedJson,
                        auth.RouterModelId,
                        body.Metadata.Model ?? "");
                },
                sendRequest: (auth, patchedJson, ct) =>
                    routerProxy.SendChatCompletionAsync(auth.Lease, patchedJson, requestId, auth.ApiKey, ct),
                onSuccess: async (auth, response, startedAt, completedAt, ct) =>
                {
                    var hasUsage = RouterUsageExtractor.TryExtract(response.Body, out var usage) && usage.TotalTokens > 0;
                    var status = hasUsage ? "completed" : "usage_missing";

                    await RecordUsageAsync(requestId, auth, body.Metadata.Model!, response.ProviderName, response.ProviderAccount, usage, status, startedAt, completedAt, endpoint: "chat_completions", stream: false);

                    logger.LogInformation(
                        "Chat completion completed. Model: {Model}, Node: {NodeId}, DurationMs: {DurationMs}, Tokens: {Tokens}",
                        body.Metadata.Model, auth.Lease.NodeId,
                        (completedAt - startedAt).TotalMilliseconds, usage.TotalTokens);

                    context.Response.StatusCode = response.StatusCode;
                    context.Response.ContentType = response.ContentType;
                    await context.Response.WriteAsync(response.Body, ct);
                },
                context.RequestAborted);
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

                try
                {
                    var patchedJson = SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId);
                    var reasoningPatch = ReasoningRequestPatcher.TryNormalize(
                        patchedJson,
                        body.Metadata.Reasoning,
                        auth.SupportsReasoning,
                        auth.AllowedReasoningEfforts ?? [],
                        auth.DefaultReasoningEffort);
                    if (!reasoningPatch.IsSuccess)
                    {
                        // Release must succeed even if client disconnected — don't cancel.
                        await controlPlaneProvider.ReleaseRouterNodeAsync(auth.Lease.LeaseId, "failed", CancellationToken.None);
                        if (lastFailedSession != null)
                        {
                            await lastFailedSession.DisposeAsync();
                            lastFailedSession = null;
                        }
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

                        // Release must succeed even if client disconnected — don't cancel.
                        await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "failed", CancellationToken.None);
                        
                        excludedNodes ??= new HashSet<string>();
                        excludedNodes.Add(lease.NodeId);

                        if (lastFailedSession != null)
                        {
                            await lastFailedSession.DisposeAsync();
                        }
                        lastFailedSession = session;

                        if (++attempts >= MaxRouterAttempts)
                        {
                            await PassThroughErrorAsync(context, lastFailedSession, context.RequestAborted);
                            return;
                        }

                        continue;
                    }

                    if (lastFailedSession != null)
                    {
                        await lastFailedSession.DisposeAsync();
                    }

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
                    // Release must succeed even if client disconnected — don't cancel.
                    await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "completed", CancellationToken.None);

                    var status = usage.TotalTokens > 0 ? "completed" : "usage_missing";
                    await RecordUsageAsync(requestId, auth, body.Metadata.Model!, providerName, providerAccount, usage, status, startedAt, completedAt, endpoint: "chat_completions", stream: true);

                    logger.LogInformation(
                        "Streaming chat completion completed. Model: {Model}, Node: {NodeId}, DurationMs: {DurationMs}, Tokens: {Tokens}",
                        body.Metadata.Model, lease.NodeId,
                        (completedAt - startedAt).TotalMilliseconds, usage.TotalTokens);
                    return;
                }
                catch (OperationCanceledException)
                {
                    // Release must succeed even if client disconnected — don't cancel.
                    await controlPlaneProvider.ReleaseRouterNodeAsync(auth.Lease.LeaseId, "failed", CancellationToken.None);
                    if (lastFailedSession != null)
                    {
                        await lastFailedSession.DisposeAsync();
                    }
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
                {
                    // Connection broke before response headers were received (e.g. node
                    // crashed mid-handshake, stale pooled HTTP/2 connection, network reset).
                    // We haven't committed the response yet, so we can still retry on
                    // another node — same semantics as a non-2xx status from the node.
                    logger.LogWarning(ex,
                        "Stream request to node {NodeId} failed before headers. RequestId: {RequestId}",
                        auth.Lease.NodeId, requestId);

                    await controlPlaneProvider.ReleaseRouterNodeAsync(auth.Lease.LeaseId, "failed", CancellationToken.None);

                    excludedNodes ??= new HashSet<string>();
                    excludedNodes.Add(auth.Lease.NodeId);

                    if (lastFailedSession != null)
                    {
                        await lastFailedSession.DisposeAsync();
                        lastFailedSession = null;
                    }

                    if (++attempts >= MaxRouterAttempts)
                    {
                        await GatewayErrors.WriteAsync(
                            context, "service_unavailable", ErrorCodes.RouterUnavailable,
                            "Upstream node connection failed", requestId);
                        return;
                    }

                    continue;
                }
                catch
                {
                    // Release must succeed even if client disconnected — don't cancel.
                    await controlPlaneProvider.ReleaseRouterNodeAsync(auth.Lease.LeaseId, "failed", CancellationToken.None);
                    if (lastFailedSession != null)
                    {
                        await lastFailedSession.DisposeAsync();
                    }
                    throw;
                }
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
        try
        {
            var statusCode = session.StatusCode;
            var body = await session.ReadErrorBodyAsync(cancellationToken);

            logger.LogError("Streaming chat completion downstream error. StatusCode: {StatusCode}, Body: {Body}", statusCode, body);

            if (context.Response.HasStarted) return;
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(body, cancellationToken);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private async Task RecordUsageAsync(
        string requestId,
        GatewayAuthorizedContext auth,
        string modelId,
        string? providerName,
        string? providerAccount,
        TokenUsage usage,
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string? endpoint = null,
        bool? stream = null)
    {
        try
        {
            await outboxRepository.EnqueueAsync(
                new GatewayUsageEvent(requestId, auth.UserId, auth.ApiKeyId, auth.ApiKeyPrefix,
                    modelId, auth.Lease.NodeId, usage, status, startedAt, completedAt,
                    providerName, providerAccount, endpoint, stream),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue usage recording. RequestId: {RequestId}", requestId);
        }
    }
}
