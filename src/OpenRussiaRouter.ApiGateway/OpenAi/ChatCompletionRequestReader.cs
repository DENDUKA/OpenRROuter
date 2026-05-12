using System.Text.Json;

namespace OpenRussiaRouter.ApiGateway.OpenAi;

/// <summary>
/// Reads only routing/billing metadata from an OpenAI-compatible chat completion request.
/// </summary>
public static class ChatCompletionRequestReader
{
    /// <summary>
    /// Reads the full request JSON and extracts routing/billing metadata.
    /// </summary>
    /// <param name="body">Request body stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw JSON plus parsed metadata.</returns>
    public static async Task<ChatCompletionRequestBody> ReadBodyAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(body, leaveOpen: false);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return new ChatCompletionRequestBody(json, Read(json));
    }

    /// <summary>
    /// Reads model id and stream flag from request JSON.
    /// </summary>
    /// <param name="body">Request body stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed request metadata.</returns>
    public static async Task<ChatCompletionRequestMetadata> ReadAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Read(document.RootElement);
    }

    /// <summary>
    /// Reads model id and stream flag from request JSON.
    /// </summary>
    /// <param name="json">Request JSON.</param>
    /// <returns>Parsed request metadata.</returns>
    public static ChatCompletionRequestMetadata Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
    }

    private static ChatCompletionRequestMetadata Read(JsonElement root)
    {
        var model = root.TryGetProperty("model", out var modelProperty)
            ? modelProperty.GetString()
            : null;
        var stream = root.TryGetProperty("stream", out var streamProperty) &&
            streamProperty.ValueKind == JsonValueKind.True;

        return new ChatCompletionRequestMetadata(model, stream);
    }
}

/// <summary>
/// Raw chat completion request JSON plus extracted metadata.
/// </summary>
/// <param name="Json">Raw JSON body.</param>
/// <param name="Metadata">Extracted metadata.</param>
public sealed record ChatCompletionRequestBody(string Json, ChatCompletionRequestMetadata Metadata);

/// <summary>
/// Metadata extracted from a chat completion request.
/// </summary>
/// <param name="Model">Requested model id.</param>
/// <param name="Stream">Whether streaming was requested.</param>
public sealed record ChatCompletionRequestMetadata(string? Model, bool Stream);
