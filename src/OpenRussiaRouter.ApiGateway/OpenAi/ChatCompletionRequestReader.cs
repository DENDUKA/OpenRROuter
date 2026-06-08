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
        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return new ChatCompletionRequestMetadata(null, false);
        }
    }

    private static ChatCompletionRequestMetadata Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ChatCompletionRequestMetadata(null, false);
        }

        var model = root.TryGetProperty("model", out var modelProperty) && modelProperty.ValueKind == JsonValueKind.String
            ? modelProperty.GetString()
            : null;
        var stream = root.TryGetProperty("stream", out var streamProperty) &&
            streamProperty.ValueKind == JsonValueKind.True;

        return new ChatCompletionRequestMetadata(model, stream, ReadReasoning(root));
    }

    private static ChatCompletionReasoningMetadata ReadReasoning(JsonElement root)
    {
        var hasReasoningEffort = root.TryGetProperty("reasoning_effort", out var flatEffortProperty) &&
            flatEffortProperty.ValueKind == JsonValueKind.String;

        var hasReasoningObject = root.TryGetProperty("reasoning", out var reasoningProperty) &&
            reasoningProperty.ValueKind == JsonValueKind.Object;

        if (!hasReasoningEffort && !hasReasoningObject)
        {
            return ChatCompletionReasoningMetadata.None;
        }

        var objectEffort = hasReasoningObject &&
                           reasoningProperty.TryGetProperty("effort", out var effortProperty) &&
                           effortProperty.ValueKind == JsonValueKind.String
            ? effortProperty.GetString()
            : null;

        return new ChatCompletionReasoningMetadata(
            hasReasoningEffort,
            hasReasoningEffort ? flatEffortProperty.GetString() : null,
            hasReasoningObject,
            objectEffort,
            hasReasoningObject && reasoningProperty.TryGetProperty("max_tokens", out _),
            hasReasoningObject && reasoningProperty.TryGetProperty("enabled", out _));
    }
}

public sealed record ChatCompletionRequestBody(string Json, ChatCompletionRequestMetadata Metadata);

public sealed record ChatCompletionRequestMetadata(string? Model, bool Stream, ChatCompletionReasoningMetadata Reasoning)
{
    public ChatCompletionRequestMetadata(string? model, bool stream)
        : this(model, stream, ChatCompletionReasoningMetadata.None)
    {
    }
}

public sealed record ChatCompletionReasoningMetadata(
    bool HasReasoningEffort,
    string? ReasoningEffort,
    bool HasReasoningObject,
    string? ReasoningObjectEffort,
    bool HasUnsupportedMaxTokens,
    bool HasUnsupportedEnabled)
{
    public static ChatCompletionReasoningMetadata None { get; } = new(false, null, false, null, false, false);
}
