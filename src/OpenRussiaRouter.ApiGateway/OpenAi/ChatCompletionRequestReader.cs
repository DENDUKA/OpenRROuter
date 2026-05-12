using System.Text.Json;

namespace OpenRussiaRouter.ApiGateway.OpenAi;

public static class ChatCompletionRequestReader
{

    public static async Task<ChatCompletionRequestBody> ReadBodyAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(body, leaveOpen: false);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return new ChatCompletionRequestBody(json, Read(json));
    }

    public static async Task<ChatCompletionRequestMetadata> ReadAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Read(document.RootElement);
    }

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

public sealed record ChatCompletionRequestBody(string Json, ChatCompletionRequestMetadata Metadata);

public sealed record ChatCompletionRequestMetadata(string? Model, bool Stream);
