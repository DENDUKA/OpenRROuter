using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ApiGateway.Application.Services;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.OpenAi;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Application.Endpoints;

/// <summary>
/// Обработчик POST /v1/images/generations.
/// Проксирует запрос image generation на ноду F9Router.
/// </summary>
public sealed class ImageGenerationHandler(
    IGatewayPipeline pipeline,
    IControlPlaneProvider controlPlaneProvider,
    IRouterProxyProvider routerProxy,
    ILogger<ImageGenerationHandler> logger)
{
    private const int MaxRouterAttempts = 5;

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

            HashSet<string>? excludedNodes = null;
            RouterProxyResponse? lastResponse = null;
            var attempts = 0;

            while (true)
            {
                var auth = await pipeline.AuthorizeAndAcquireLeaseAsync(context, model, requestId, excludedNodes, context.RequestAborted);

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
                    var patchedJson = SseRequestPatcher.PatchModel(json, auth.RouterModelId);
                    var startedAt = DateTimeOffset.UtcNow;

                    var response = await routerProxy.SendImageGenerationAsync(auth.Lease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);
                    var completedAt = DateTimeOffset.UtcNow;

                    bool isError = response.StatusCode >= 400;

                    await controlPlaneProvider.ReleaseRouterNodeAsync(
                        auth.Lease.LeaseId,
                        isError ? "failed" : "completed",
                        CancellationToken.None);

                    if (isError)
                    {
                        logger.LogWarning("Image generation downstream error. StatusCode: {StatusCode}, Node: {NodeId}, Body: {Body}", response.StatusCode, auth.Lease.NodeId, response.Body);

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

                    logger.LogInformation(
                        "Image generation completed. Model: {Model}, Node: {NodeId}, DurationMs: {DurationMs}",
                        model, auth.Lease.NodeId,
                        (completedAt - startedAt).TotalMilliseconds);

                    context.Response.StatusCode = response.StatusCode;
                    context.Response.ContentType = response.ContentType;
                    await context.Response.WriteAsync(response.Body);
                    return;
                }
                catch
                {
                    await controlPlaneProvider.ReleaseRouterNodeAsync(auth.Lease.LeaseId, "failed", CancellationToken.None);
                    throw;
                }
            }
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
