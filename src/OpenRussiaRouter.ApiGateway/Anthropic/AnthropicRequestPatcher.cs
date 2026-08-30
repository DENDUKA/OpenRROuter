using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenRussiaRouter.ApiGateway.Anthropic;

public static class AnthropicRequestPatcher
{
    private static readonly string[] ReasoningFieldsToRemove =
    [
        "reasoning_effort", "reasoning", "thinkingConfig",
        "enable_thinking", "thinking_budget"
    ];

    public static void PatchModel(JsonNode node, string routerModelId)
    {
        if (node is JsonObject obj)
        {
            obj["model"] = routerModelId;
        }
    }

    public static string PatchModel(string json, string routerModelId)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                obj["model"] = routerModelId;
                return obj.ToJsonString();
            }
        }
        catch (JsonException)
        {
        }

        return json;
    }

    public static AnthropicReasoningPatchResult TryNormalizeReasoningInPlace(
        JsonNode node,
        AnthropicReasoningMetadata reasoning,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts,
        string? defaultEffort = null)
    {
        if (node is not JsonObject obj)
            return AnthropicReasoningPatchResult.InPlaceSuccess(AnthropicReasoningPatchAction.Unchanged);

        return NormalizeReasoningObject(obj, reasoning, supportsReasoning, allowedEfforts, defaultEffort);
    }

    private static AnthropicReasoningPatchResult NormalizeReasoningObject(
        JsonObject obj,
        AnthropicReasoningMetadata reasoning,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts,
        string? defaultEffort)
    {
        var allowed = NormalizeAllowedEfforts(allowedEfforts);

        // Always clean up non-standard reasoning fields
        foreach (var field in ReasoningFieldsToRemove)
            obj.Remove(field);

        if (!supportsReasoning || allowed.Count == 0)
        {
            // Model doesn't support reasoning: strip output_config and thinking
            obj.Remove("output_config");
            obj.Remove("thinking");
            return AnthropicReasoningPatchResult.InPlaceSuccess(AnthropicReasoningPatchAction.Stripped);
        }

        // Determine requested effort from output_config.effort or fallback reasoning_effort
        var requestedEffort = NormalizeOrNull(reasoning.OutputConfigEffort) ?? NormalizeOrNull(reasoning.ReasoningEffort);

        // If thinking: {type: "disabled"} is explicitly provided without output_config
        if (reasoning.HasThinking && string.Equals(reasoning.ThinkingType, "disabled", StringComparison.OrdinalIgnoreCase) && requestedEffort is null)
        {
            if (allowed.Contains("none", StringComparer.Ordinal))
            {
                SetOutputConfigEffort(obj, "none");
                return AnthropicReasoningPatchResult.InPlaceSuccess(AnthropicReasoningPatchAction.Applied);
            }
        }

        var validSelection = requestedEffort is not null && allowed.Contains(requestedEffort, StringComparer.Ordinal);
        var selected = validSelection
            ? requestedEffort
            : PickDefault(allowed, defaultEffort);

        if (selected is null)
        {
            obj.Remove("output_config");
            obj.Remove("thinking");
            return AnthropicReasoningPatchResult.InPlaceSuccess(AnthropicReasoningPatchAction.Stripped);
        }

        SetOutputConfigEffort(obj, selected);

        return AnthropicReasoningPatchResult.InPlaceSuccess(
            validSelection ? AnthropicReasoningPatchAction.Applied : AnthropicReasoningPatchAction.Defaulted);
    }

    private static void SetOutputConfigEffort(JsonObject obj, string effort)
    {
        // 9Router Anthropic converter and downstream models understand output_config.effort
        if (obj.TryGetPropertyValue("output_config", out var existingNode) && existingNode is JsonObject outputConfigObj)
        {
            outputConfigObj["effort"] = effort;
        }
        else
        {
            obj["output_config"] = new JsonObject
            {
                ["effort"] = effort
            };
        }
    }

    private static string? PickDefault(IReadOnlyList<string> allowed, string? defaultEffort)
    {
        var normalized = NormalizeOrNull(defaultEffort);
        if (normalized is not null && allowed.Contains(normalized, StringComparer.Ordinal))
            return normalized;

        return allowed.FirstOrDefault();
    }

    private static string? NormalizeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static IReadOnlyList<string> NormalizeAllowedEfforts(IReadOnlyList<string> allowedEfforts) =>
        allowedEfforts
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

public enum AnthropicReasoningPatchAction
{
    Unchanged,
    Applied,
    Defaulted,
    Stripped
}

public sealed record AnthropicReasoningPatchResult(
    bool IsSuccess,
    string? PatchedJson,
    AnthropicReasoningPatchAction Action)
{
    public static AnthropicReasoningPatchResult InPlaceSuccess(AnthropicReasoningPatchAction action) =>
        new(true, null, action);
}
