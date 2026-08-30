using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenRussiaRouter.ApiGateway.Anthropic;

public static class AnthropicRequestReader
{
    public static async Task<AnthropicRequestBodyNode> ReadBodyNodeAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(body, leaveOpen: false);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var node = JsonNode.Parse(json);
            return new AnthropicRequestBodyNode(node, json, ReadMetadata(json));
        }
        catch (JsonException)
        {
            return new AnthropicRequestBodyNode(null, json, new AnthropicRequestMetadata(null, false, AnthropicReasoningMetadata.None));
        }
    }

    public static AnthropicRequestMetadata ReadMetadata(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadMetadata(document.RootElement);
        }
        catch (JsonException)
        {
            return new AnthropicRequestMetadata(null, false, AnthropicReasoningMetadata.None);
        }
    }

    private static AnthropicRequestMetadata ReadMetadata(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new AnthropicRequestMetadata(null, false, AnthropicReasoningMetadata.None);
        }

        var model = root.TryGetProperty("model", out var modelProperty) && modelProperty.ValueKind == JsonValueKind.String
            ? modelProperty.GetString()
            : null;

        var stream = root.TryGetProperty("stream", out var streamProperty) &&
            streamProperty.ValueKind == JsonValueKind.True;

        return new AnthropicRequestMetadata(model, stream, ReadReasoning(root));
    }

    private static AnthropicReasoningMetadata ReadReasoning(JsonElement root)
    {
        // 1. Anthropic output_config.effort
        string? outputConfigEffort = null;
        var hasOutputConfig = root.TryGetProperty("output_config", out var outputConfigProperty) &&
                              outputConfigProperty.ValueKind == JsonValueKind.Object;
        if (hasOutputConfig && outputConfigProperty.TryGetProperty("effort", out var effortProperty) &&
            effortProperty.ValueKind == JsonValueKind.String)
        {
            outputConfigEffort = effortProperty.GetString();
        }

        // 2. Anthropic thinking block: {"thinking": {"type": "enabled", "budget_tokens": ...}}
        var hasThinking = root.TryGetProperty("thinking", out var thinkingProperty) &&
                          thinkingProperty.ValueKind == JsonValueKind.Object;
        string? thinkingType = null;
        long? thinkingBudget = null;
        if (hasThinking)
        {
            if (thinkingProperty.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
            {
                thinkingType = typeProp.GetString();
            }
            if (thinkingProperty.TryGetProperty("budget_tokens", out var budgetProp) && budgetProp.TryGetInt64(out var b))
            {
                thinkingBudget = b;
            }
        }

        // 3. Fallback/compatibility OpenAI-style reasoning_effort
        string? reasoningEffort = null;
        if (root.TryGetProperty("reasoning_effort", out var flatEffort) && flatEffort.ValueKind == JsonValueKind.String)
        {
            reasoningEffort = flatEffort.GetString();
        }

        return new AnthropicReasoningMetadata(
            hasOutputConfig,
            outputConfigEffort,
            hasThinking,
            thinkingType,
            thinkingBudget,
            reasoningEffort);
    }
}

public sealed record AnthropicRequestBodyNode(JsonNode? Node, string RawJson, AnthropicRequestMetadata Metadata);

public sealed record AnthropicRequestMetadata(string? Model, bool Stream, AnthropicReasoningMetadata Reasoning);

public sealed record AnthropicReasoningMetadata(
    bool HasOutputConfig,
    string? OutputConfigEffort,
    bool HasThinking,
    string? ThinkingType,
    long? ThinkingBudget,
    string? ReasoningEffort)
{
    public static AnthropicReasoningMetadata None { get; } = new(false, null, false, null, null, null);
}
