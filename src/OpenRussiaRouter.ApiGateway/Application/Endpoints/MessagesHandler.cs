using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ApiGateway.Anthropic;
using OpenRussiaRouter.ApiGateway.Application.Extensions;
using OpenRussiaRouter.ApiGateway.Application.Services;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Application.Endpoints;

/// <summary>
/// Обработчик POST /v1/messages для Anthropic Messages API (Claude Code).
/// Не направляет Anthropic-тело в ChatCompletionHandler, сохраняет Anthropic-специфичные заголовки и SSE семантику.
/// </summary>
public sealed class MessagesHandler(
    IGatewayPipeline pipeline,
    IUsageOutboxRepository outboxRepository,
    IControlPlaneProvider controlPlaneProvider,
    IRouterProxyProvider routerProxy,
    IOptions<GatewayRetryOptions> retryOptions,
    ILogger<MessagesHandler> logger)
{
    private int MaxRouterAttempts => retryOptions.Value.MaxRouterAttempts;

    public async Task HandleAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);

        try
        {
            var body = await AnthropicRequestReader.ReadBodyNodeAsync(context.Request.Body, context.RequestAborted);

            if (body.Metadata.Stream)
            {
                await ExecuteStreamingAsync(context, body, requestId);
                return;
            }

            await ExecuteNonStreamingAsync(context, body, requestId);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected before the response completed — expected, not an error.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Messages handling failed. RequestId: {RequestId}", requestId);
            await GatewayErrors.WriteAnthropicAsync(context, "api_error", ErrorCodes.InternalError, "Internal server error", requestId);
        }
    }

    private async Task ExecuteNonStreamingAsync(HttpContext context, AnthropicRequestBodyNode body, string requestId)
    {
        var authorization = await pipeline.AuthorizeAsync(context, body.Metadata.Model, requestId, context.RequestAborted, body.Node);
        if (authorization is null)
            return;

        HashSet<string>? excludedNodes = null;
        RouterProxyResponse? lastResponse = null;
        var attempts = 0;
        var queryString = context.Request.QueryString.Value;

        while (true)
        {
            var auth = await pipeline.AcquireLeaseAsync(context, authorization, requestId, excludedNodes, context.RequestAborted);
            if (auth is null)
            {
                if (lastResponse != null)
                {
                    context.Response.StatusCode = lastResponse.StatusCode;
                    context.Response.ContentType = lastResponse.ContentType;
                    await context.Response.WriteAsync(lastResponse.Body, context.RequestAborted);
                }
                return;
            }

            var lease = auth.Lease;
            var startedAt = DateTimeOffset.UtcNow;

            try
            {
                string patchedJson;
                if (body.Node is not null)
                {
                    AnthropicRequestPatcher.PatchModel(body.Node, auth.RouterModelId);
                    var reasoningPatch = AnthropicRequestPatcher.TryNormalizeReasoningInPlace(
                        body.Node,
                        body.Metadata.Reasoning,
                        auth.SupportsReasoning,
                        auth.AllowedReasoningEfforts ?? [],
                        auth.DefaultReasoningEffort);

                    logger.LogDebug("Anthropic reasoning normalization: {Action}. Model: {Model}, Node: {NodeId}",
                        reasoningPatch.Action, body.Metadata.Model, lease.NodeId);

                    patchedJson = body.Node.ToJsonString();
                }
                else
                {
                    patchedJson = AnthropicRequestPatcher.PatchModel(body.RawJson, auth.RouterModelId);
                }

                var response = await routerProxy.SendMessagesAsync(
                    lease,
                    patchedJson,
                    requestId,
                    auth.ApiKey,
                    context.Request.Headers,
                    queryString,
                    context.RequestAborted);

                var isUpstreamError = response.StatusCode >= 500;
                var releaseStatus = isUpstreamError ? "failed" : "completed";
                await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, releaseStatus, CancellationToken.None);

                if (isUpstreamError)
                {
                    logger.LogWarning("Upstream node {NodeId} returned {StatusCode} for messages. RequestId: {RequestId}",
                        lease.NodeId, response.StatusCode, requestId);

                    excludedNodes ??= new HashSet<string>();
                    excludedNodes.Add(lease.NodeId);
                    lastResponse = response;

                    if (++attempts >= MaxRouterAttempts)
                    {
                        context.Response.StatusCode = response.StatusCode;
                        context.Response.ContentType = response.ContentType;
                        await context.Response.WriteAsync(response.Body, context.RequestAborted);
                        return;
                    }

                    continue;
                }

                var completedAt = DateTimeOffset.UtcNow;
                var hasUsage = AnthropicUsageExtractor.TryExtract(response.Body, out var usage) && usage.TotalTokens > 0;
                var status = hasUsage ? "completed" : "usage_missing";

                await RecordUsageAsync(requestId, auth, body.Metadata.Model!, response.ProviderName, response.ProviderAccount, usage, status, startedAt, completedAt, endpoint: "messages", stream: false);

                logger.LogInformation(
                    "Messages completion completed. Model: {Model}, Node: {NodeId}, DurationMs: {DurationMs}, Tokens: {Tokens}",
                    body.Metadata.Model, lease.NodeId,
                    (completedAt - startedAt).TotalMilliseconds, usage.TotalTokens);

                context.Response.StatusCode = response.StatusCode;
                context.Response.ContentType = response.ContentType;
                await context.Response.WriteAsync(response.Body, context.RequestAborted);
                return;
            }
            catch (OperationCanceledException)
            {
                await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "failed", CancellationToken.None);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                logger.LogWarning(ex, "Messages request to node {NodeId} failed. RequestId: {RequestId}", lease.NodeId, requestId);

                await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "failed", CancellationToken.None);

                excludedNodes ??= new HashSet<string>();
                excludedNodes.Add(lease.NodeId);

                if (++attempts >= MaxRouterAttempts)
                {
                    if (lastResponse != null)
                    {
                        context.Response.StatusCode = lastResponse.StatusCode;
                        context.Response.ContentType = lastResponse.ContentType;
                        await context.Response.WriteAsync(lastResponse.Body, context.RequestAborted);
                    }
                    else
                    {
                        await GatewayErrors.WriteAnthropicAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "Upstream node connection failed", requestId);
                    }
                    return;
                }
            }
            catch
            {
                await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "failed", CancellationToken.None);
                throw;
            }
        }
    }

    private async Task ExecuteStreamingAsync(HttpContext context, AnthropicRequestBodyNode body, string requestId)
    {
        var authorization = await pipeline.AuthorizeAsync(context, body.Metadata.Model, requestId, context.RequestAborted, body.Node);
        if (authorization is null)
            return;

        HashSet<string>? excludedNodes = null;
        AnthropicStreamSession? lastFailedSession = null;
        var attempts = 0;
        var queryString = context.Request.QueryString.Value;

        while (true)
        {
            var auth = await pipeline.AcquireLeaseAsync(context, authorization, requestId, excludedNodes, context.RequestAborted);
            if (auth is null)
            {
                if (lastFailedSession != null)
                {
                    await PassThroughErrorAsync(context, lastFailedSession, context.RequestAborted);
                }
                return;
            }

            var lease = auth.Lease;

            try
            {
                string patchedJson;
                if (body.Node is not null)
                {
                    AnthropicRequestPatcher.PatchModel(body.Node, auth.RouterModelId);
                    var reasoningPatch = AnthropicRequestPatcher.TryNormalizeReasoningInPlace(
                        body.Node,
                        body.Metadata.Reasoning,
                        auth.SupportsReasoning,
                        auth.AllowedReasoningEfforts ?? [],
                        auth.DefaultReasoningEffort);

                    logger.LogDebug("Anthropic streaming reasoning normalization: {Action}. Model: {Model}, Node: {NodeId}",
                        reasoningPatch.Action, body.Metadata.Model, lease.NodeId);

                    patchedJson = body.Node.ToJsonString();
                }
                else
                {
                    patchedJson = AnthropicRequestPatcher.PatchModel(body.RawJson, auth.RouterModelId);
                }

                var session = await routerProxy.BeginStreamMessagesAsync(
                    lease,
                    patchedJson,
                    requestId,
                    auth.ApiKey,
                    context.Request.Headers,
                    queryString,
                    context.RequestAborted);

                if (!session.IsSuccess)
                {
                    var errorBody = await session.ReadErrorBodyAsync(context.RequestAborted);
                    logger.LogWarning("Messages stream downstream error. StatusCode: {StatusCode}, Node: {NodeId}, Body: {Body}",
                        session.StatusCode, lease.NodeId, errorBody);

                    await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "failed", CancellationToken.None);

                    // 4xx errors should not retry across nodes (Anthropic client error / validation error)
                    if (session.StatusCode >= 400 && session.StatusCode < 500)
                    {
                        context.Response.StatusCode = session.StatusCode;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(errorBody, context.RequestAborted);
                        await session.DisposeAsync();
                        return;
                    }

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
                await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "completed", CancellationToken.None);

                var status = usage.TotalTokens > 0 ? "completed" : "usage_missing";
                await RecordUsageAsync(requestId, auth, body.Metadata.Model!, providerName, providerAccount, usage, status, startedAt, completedAt, endpoint: "messages", stream: true);

                logger.LogInformation(
                    "Streaming messages completed. Model: {Model}, Node: {NodeId}, DurationMs: {DurationMs}, Tokens: {Tokens}",
                    body.Metadata.Model, lease.NodeId,
                    (completedAt - startedAt).TotalMilliseconds, usage.TotalTokens);
                return;
            }
            catch (OperationCanceledException)
            {
                await controlPlaneProvider.ReleaseRouterNodeAsync(auth.Lease.LeaseId, "failed", CancellationToken.None);
                if (lastFailedSession != null)
                {
                    await lastFailedSession.DisposeAsync();
                }
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                logger.LogWarning(ex, "Stream request to node {NodeId} failed before headers. RequestId: {RequestId}",
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
                    await GatewayErrors.WriteAnthropicAsync(
                        context, "service_unavailable", ErrorCodes.RouterUnavailable,
                        "Upstream node connection failed", requestId);
                    return;
                }

                continue;
            }
            catch
            {
                await controlPlaneProvider.ReleaseRouterNodeAsync(auth.Lease.LeaseId, "failed", CancellationToken.None);
                if (lastFailedSession != null)
                {
                    await lastFailedSession.DisposeAsync();
                }
                throw;
            }
        }
    }

    private async Task PassThroughErrorAsync(HttpContext context, AnthropicStreamSession session, CancellationToken cancellationToken)
    {
        try
        {
            var statusCode = session.StatusCode;
            var body = await session.ReadErrorBodyAsync(cancellationToken);

            logger.LogError("Streaming messages downstream error. StatusCode: {StatusCode}, Body: {Body}", statusCode, body);

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
