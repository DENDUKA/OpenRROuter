using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenRussiaRouter.ApiGateway.OpenAi;

/// <summary>
/// Normalizes all client reasoning dialects into the single field understood by 9Router.
/// Invalid client settings never reject an otherwise valid completion request.
/// </summary>
public static class ReasoningRequestPatcher
{
    private static readonly string[] ReasoningFields =
    [
        "reasoning_effort", "reasoning", "thinking", "thinkingConfig",
        "enable_thinking", "thinking_budget", "output_config"
    ];

    public static ReasoningPatchResult TryNormalize(
        string requestJson,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts,
        string? defaultEffort = null) =>
        TryNormalize(requestJson, ChatCompletionRequestReader.Read(requestJson).Reasoning,
            supportsReasoning, allowedEfforts, defaultEffort);

    public static ReasoningPatchResult TryNormalize(
        string requestJson,
        ChatCompletionReasoningMetadata reasoning,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts,
        string? defaultEffort = null)
    {
        try
        {
            var node = JsonNode.Parse(requestJson);
            if (node is not JsonObject obj)
                return ReasoningPatchResult.Success(requestJson, ReasoningPatchAction.Unchanged);

            var result = NormalizeObject(obj, reasoning, supportsReasoning, allowedEfforts, defaultEffort);
            return result with { PatchedJson = obj.ToJsonString() };
        }
        catch (JsonException)
        {
            return ReasoningPatchResult.Success(requestJson, ReasoningPatchAction.Unchanged);
        }
    }

    public static ReasoningPatchResult TryNormalizeInPlace(
        JsonNode node,
        ChatCompletionReasoningMetadata reasoning,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts,
        string? defaultEffort = null) =>
        node is JsonObject obj
            ? NormalizeObject(obj, reasoning, supportsReasoning, allowedEfforts, defaultEffort)
            : ReasoningPatchResult.InPlaceSuccess(ReasoningPatchAction.Unchanged);

    private static ReasoningPatchResult NormalizeObject(
        JsonObject obj,
        ChatCompletionReasoningMetadata reasoning,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts,
        string? defaultEffort)
    {
        var allowed = NormalizeAllowedEfforts(allowedEfforts);
        foreach (var field in ReasoningFields)
            obj.Remove(field);

        if (!supportsReasoning || allowed.Count == 0)
            return ReasoningPatchResult.InPlaceSuccess(ReasoningPatchAction.Stripped);

        var flat = NormalizeOrNull(reasoning.ReasoningEffort);
        var nested = NormalizeOrNull(reasoning.ReasoningObjectEffort);
        var validSelection = !reasoning.HasUnsupportedEnabled && !reasoning.HasUnsupportedMaxTokens &&
            !(flat is not null && nested is not null && !string.Equals(flat, nested, StringComparison.Ordinal)) &&
            (flat ?? nested) is { } requested && allowed.Contains(requested, StringComparer.Ordinal);

        var selected = validSelection
            ? flat ?? nested
            : PickDefault(allowed, defaultEffort);

        if (selected is null)
            return ReasoningPatchResult.InPlaceSuccess(ReasoningPatchAction.Stripped);

        obj["reasoning_effort"] = selected;
        return ReasoningPatchResult.InPlaceSuccess(
            validSelection ? ReasoningPatchAction.Applied : ReasoningPatchAction.Defaulted);
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

public enum ReasoningPatchAction
{
    Unchanged,
    Applied,
    Defaulted,
    Stripped
}

public sealed record ReasoningPatchResult(
    bool IsSuccess,
    string? PatchedJson,
    ReasoningPatchAction Action,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static ReasoningPatchResult Success(string patchedJson, ReasoningPatchAction action) =>
        new(true, patchedJson, action);

    public static ReasoningPatchResult InPlaceSuccess(ReasoningPatchAction action) =>
        new(true, null, action);
}
