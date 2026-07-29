using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ApiGateway.Application.Services;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Application.Endpoints;

/// <summary>
/// Обработчик POST /v1/audio/speech.
/// Проксирует TTS-запрос на ноду F9Router.
/// Полная совместимость с OpenAI TTS API: model, input, voice, response_format, speed, и т.д.
/// Биллинг: количество символов Unicode (code points) входного текста = output_tokens.
/// </summary>
public sealed class TextToSpeechHandler(
    RouterRetryExecutor retryExecutor,
    IUsageOutboxRepository outboxRepository,
    IRouterProxyProvider routerProxy,
    ILogger<TextToSpeechHandler> logger)
{
    public async Task HandleAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);

        try
        {
            using var reader = new StreamReader(context.Request.Body, leaveOpen: false);
            var json = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);

            var (model, input, voice) = ExtractRequiredFields(json);
            if (string.IsNullOrWhiteSpace(model))
            {
                await GatewayErrors.WriteAsync(context, "invalid_request_error", ErrorCodes.ValidationFailed, "model is required", requestId);
                return;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                await GatewayErrors.WriteAsync(context, "invalid_request_error", ErrorCodes.ValidationFailed, "input is required", requestId);
                return;
            }

            if (string.IsNullOrWhiteSpace(voice))
            {
                await GatewayErrors.WriteAsync(context, "invalid_request_error", ErrorCodes.ValidationFailed, "voice is required", requestId);
                return;
            }

            await retryExecutor.ExecuteAsync(
                context,
                model,
                requestId,
                patchBody: auth => PatchModelAndVoice(json, auth.RouterModelId, voice),
                sendRequest: (auth, patchedJson, ct) =>
                    routerProxy.SendTextToSpeechAsync(auth.Lease, patchedJson, requestId, auth.ApiKey, ct),
                onSuccess: async (auth, response, startedAt, completedAt, ct) =>
                {
                    // Биллинг: количество символов Unicode (code points) = output_tokens
                    var characters = (long)input.EnumerateRunes().Count();
                    var usage = new TokenUsage(InputTokens: 0, OutputTokens: characters, CacheTokens: 0);
                    var status = characters > 0 ? "completed" : "usage_missing";

                    await RecordUsageAsync(requestId, auth, model, response.ProviderName, response.ProviderAccount, usage, status, startedAt, completedAt, endpoint: "tts", stream: false);

                    logger.LogInformation(
                        "TTS completed. Model: {Model}, Voice: {Voice}, Node: {NodeId}, DurationMs: {DurationMs}, Characters: {Characters}",
                        model, voice, auth.Lease.NodeId,
                        (completedAt - startedAt).TotalMilliseconds, characters);

                    context.Response.StatusCode = response.StatusCode;
                    context.Response.ContentType = response.ContentType;
                    if (response.BodyStream is { } stream)
                    {
                        await using (stream)
                        {
                            await stream.CopyToAsync(context.Response.Body, ct);
                        }
                    }
                    else if (response.BodyBytes is { Length: > 0 } bytes)
                    {
                        await context.Response.BodyWriter.WriteAsync(bytes, ct);
                    }
                    else
                    {
                        await context.Response.WriteAsync(response.Body, ct);
                    }
                },
                context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TTS handling failed. RequestId: {RequestId}", requestId);
            await GatewayErrors.WriteAsync(context, "internal_error", ErrorCodes.InternalError, "Internal server error", requestId);
        }
    }

    /// <summary>
    /// Патч JSON: заменяет model на routerModelId и voice на новый voice.
    /// 9Router ожидает формат model/voice для парсинга.
    /// </summary>
    private static string PatchModelAndVoice(string json, string routerModelId, string voice)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name.Equals("model", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteString("model", routerModelId);
                    }
                    else if (property.Name.Equals("voice", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteString("voice", voice);
                    }
                    else
                    {
                        property.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static (string? Model, string? Input, string? Voice) ExtractRequiredFields(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            string? model = null;
            string? input = null;
            string? voice = null;

            if (root.TryGetProperty("model", out var modelProperty) && modelProperty.ValueKind == JsonValueKind.String)
            {
                model = modelProperty.GetString();
            }

            if (root.TryGetProperty("input", out var inputProperty) && inputProperty.ValueKind == JsonValueKind.String)
            {
                input = inputProperty.GetString();
            }

            if (root.TryGetProperty("voice", out var voiceProperty) && voiceProperty.ValueKind == JsonValueKind.String)
            {
                voice = voiceProperty.GetString();
            }

            return (model, input, voice);
        }
        catch (JsonException)
        {
            return (null, null, null);
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
