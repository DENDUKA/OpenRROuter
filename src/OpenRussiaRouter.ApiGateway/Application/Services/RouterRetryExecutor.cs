using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ApiGateway.Application.Extensions;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.ApiGateway.Application.Services;

/// <summary>
/// Общий retry/failover цикл для нестриминговых POST-хендлеров (chat, image, tts).
/// Управляет excluded-nodes, подсчётом попыток и освобождением лиз.
/// </summary>
public sealed class RouterRetryExecutor(
    IGatewayPipeline pipeline,
    IControlPlaneProvider controlPlaneProvider,
    IOptions<GatewayRetryOptions> retryOptions,
    ILogger<RouterRetryExecutor> logger)
{
    private int MaxRouterAttempts => retryOptions.Value.MaxRouterAttempts;

    /// <summary>
    /// Выполняет запрос с retry на других нодах при 5xx / обрыве соединения.
    /// </summary>
    /// <param name="context">HTTP-контекст запроса.</param>
    /// <param name="model">Публичный ID модели (для авторизации).</param>
    /// <param name="requestId">Correlation ID.</param>
    /// <param name="patchBody">Патч JSON-тела запроса (подмена модели и т.п.).</param>
    /// <param name="sendRequest">Отправка запроса на ноду.</param>
    /// <param name="onSuccess">Обработка успешного ответа (запись в response, usage и т.п.).</param>
    /// <param name="cancellationToken">Токен отмены от клиента.</param>
    public async Task ExecuteAsync(
        HttpContext context,
        string model,
        string requestId,
        Func<GatewayAuthorizedContext, string> patchBody,
        Func<GatewayAuthorizedContext, string, CancellationToken, Task<RouterProxyResponse>> sendRequest,
        Func<GatewayAuthorizedContext, RouterProxyResponse, DateTimeOffset, DateTimeOffset, CancellationToken, Task> onSuccess,
        CancellationToken cancellationToken)
    {
        HashSet<string>? excludedNodes = null;
        RouterProxyResponse? lastResponse = null;
        var attempts = 0;

        while (true)
        {
            var auth = await pipeline.AuthorizeAndAcquireLeaseAsync(
                context, model, requestId, excludedNodes, cancellationToken);

            // Если auth == null на первой попытке — pipeline уже записал ошибку в ответ.
            // На последующих — ноды кончились.
            if (auth is null)
            {
                if (lastResponse != null)
                {
                    context.Response.StatusCode = lastResponse.StatusCode;
                    context.Response.ContentType = lastResponse.ContentType;
                    await context.Response.WriteAsync(lastResponse.Body);
                }
                return;
            }

            try
            {
                var patchedJson = patchBody(auth);
                var startedAt = DateTimeOffset.UtcNow;
                var response = await sendRequest(auth, patchedJson, cancellationToken);
                var completedAt = DateTimeOffset.UtcNow;

                var isError = response.StatusCode >= 400;

                // Release must succeed even if client disconnected — don't cancel.
                await controlPlaneProvider.ReleaseRouterNodeAsync(
                    auth.Lease.LeaseId,
                    isError ? "failed" : "completed",
                    CancellationToken.None);

                if (isError)
                {
                    logger.LogWarning(
                        "Downstream error. StatusCode: {StatusCode}, Node: {NodeId}, RequestId: {RequestId}",
                        response.StatusCode, auth.Lease.NodeId, requestId);

                    excludedNodes ??= new HashSet<string>();
                    excludedNodes.Add(auth.Lease.NodeId);
                    lastResponse = response;

                    if (++attempts >= MaxRouterAttempts)
                    {
                        context.Response.StatusCode = lastResponse.StatusCode;
                        context.Response.ContentType = lastResponse.ContentType;
                        await context.Response.WriteAsync(lastResponse.Body);
                        return;
                    }

                    continue;
                }

                await onSuccess(auth, response, startedAt, completedAt, cancellationToken);
                return;
            }
            catch (RoutingValidationException ex)
            {
                // Release must succeed even if client disconnected — don't cancel.
                await controlPlaneProvider.ReleaseRouterNodeAsync(
                    auth.Lease.LeaseId, "failed", CancellationToken.None);

                await GatewayErrors.WriteAsync(
                    context,
                    "invalid_request_error",
                    ex.ErrorCode ?? ErrorCodes.ValidationFailed,
                    ex.Message,
                    requestId);
                return;
            }
            catch (OperationCanceledException)
            {
                // Release must succeed even if client disconnected — don't cancel.
                await controlPlaneProvider.ReleaseRouterNodeAsync(
                    auth.Lease.LeaseId, "failed", CancellationToken.None);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                // Connection broke before/while receiving the response (e.g. node
                // crashed mid-stream, stale pooled HTTP/2 connection, network reset).
                // Treat like a 5xx downstream error: release, exclude the node, retry.
                logger.LogWarning(ex,
                    "Request to node {NodeId} failed before response. RequestId: {RequestId}",
                    auth.Lease.NodeId, requestId);

                await controlPlaneProvider.ReleaseRouterNodeAsync(
                    auth.Lease.LeaseId, "failed", CancellationToken.None);

                excludedNodes ??= new HashSet<string>();
                excludedNodes.Add(auth.Lease.NodeId);

                if (++attempts >= MaxRouterAttempts)
                {
                    if (lastResponse != null)
                    {
                        context.Response.StatusCode = lastResponse.StatusCode;
                        context.Response.ContentType = lastResponse.ContentType;
                        await context.Response.WriteAsync(lastResponse.Body);
                    }
                    else
                    {
                        await GatewayErrors.WriteAsync(
                            context, "service_unavailable", ErrorCodes.RouterUnavailable,
                            "Upstream node connection failed", requestId);
                    }
                    return;
                }

                continue;
            }
            catch
            {
                // Release must succeed even if client disconnected — don't cancel.
                await controlPlaneProvider.ReleaseRouterNodeAsync(
                    auth.Lease.LeaseId, "failed", CancellationToken.None);
                throw;
            }
        }
    }
}
