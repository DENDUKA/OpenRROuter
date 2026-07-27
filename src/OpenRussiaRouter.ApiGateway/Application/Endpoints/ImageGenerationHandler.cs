using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ApiGateway.Application.Services;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.OpenAi;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.ApiGateway.Application.Endpoints;

/// <summary>
/// Обработчик POST /v1/images/generations.
/// Проксирует запрос image generation на ноду F9Router.
/// </summary>
public sealed class ImageGenerationHandler(
    RouterRetryExecutor retryExecutor,
    IRouterProxyProvider routerProxy,
    ILogger<ImageGenerationHandler> logger)
{
    public async Task HandleAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);

        try
        {
            using var reader = new StreamReader(context.Request.Body, leaveOpen: false);
            var json = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);

            var model = ExtractModel(json);
            if (string.IsNullOrWhiteSpace(model))
            {
                await GatewayErrors.WriteAsync(context, "invalid_request_error", ErrorCodes.ValidationFailed, "model is required", requestId);
                return;
            }

            await retryExecutor.ExecuteAsync(
                context,
                model,
                requestId,
                patchBody: auth => SseRequestPatcher.PatchModel(json, auth.RouterModelId),
                sendRequest: (auth, patchedJson, ct) =>
                    routerProxy.SendImageGenerationAsync(auth.Lease, patchedJson, requestId, auth.ApiKey, ct),
                onSuccess: (auth, response, startedAt, completedAt, ct) =>
                {
                    logger.LogInformation(
                        "Image generation completed. Model: {Model}, Node: {NodeId}, DurationMs: {DurationMs}",
                        model, auth.Lease.NodeId,
                        (completedAt - startedAt).TotalMilliseconds);

                    context.Response.StatusCode = response.StatusCode;
                    context.Response.ContentType = response.ContentType;
                    return context.Response.WriteAsync(response.Body, ct);
                },
                context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Image generation handling failed. RequestId: {RequestId}", requestId);
            await GatewayErrors.WriteAsync(context, "internal_error", ErrorCodes.InternalError, "Internal server error", requestId);
        }
    }

    private static string? ExtractModel(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("model", out var modelProperty) &&
                modelProperty.ValueKind == JsonValueKind.String)
            {
                return modelProperty.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }
}
