using System.Text.Json;
using System.Text.Json.Nodes;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.ApiGateway.OpenAi;

public static class ReasoningRequestPatcher
{
    public static ReasoningPatchResult TryNormalize(
        string requestJson,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts,
        string? defaultEffort = null)
    {
        return TryNormalize(
            requestJson,
            ChatCompletionRequestReader.Read(requestJson).Reasoning,
            supportsReasoning,
            allowedEfforts,
            defaultEffort);
    }

    public static ReasoningPatchResult TryNormalize(
        string requestJson,
        ChatCompletionReasoningMetadata reasoning,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts,
        string? defaultEffort = null)
    {
        var normalizedAllowed = NormalizeAllowedEfforts(allowedEfforts);
        var normalizedDefault = NormalizeOrNull(defaultEffort);
        // Defensive: if admin-curated default is somehow not in the allowed list, ignore it
        // (treat as if no default were set) rather than silently injecting an unsupported effort.
        if (normalizedDefault is not null && normalizedAllowed.Count > 0
            && !normalizedAllowed.Contains(normalizedDefault, StringComparer.Ordinal))
        {
            normalizedDefault = null;
        }

        // Case 1: client sent no reasoning fields at all.
        // Apply the model's default effort (if any) so that admin-configured defaults take
        // effect even when clients (e.g. raw curl) never set reasoning_effort.
        if (!reasoning.HasReasoningEffort && !reasoning.HasReasoningObject)
        {
            if (supportsReasoning && normalizedDefault is not null)
            {
                return InjectEffort(requestJson, normalizedDefault);
            }
            return ReasoningPatchResult.Success(requestJson);
        }

        if (reasoning.HasUnsupportedMaxTokens || reasoning.HasUnsupportedEnabled)
        {
            return ReasoningPatchResult.Error(
                ErrorCodes.UnsupportedReasoning,
                "reasoning.max_tokens/enabled not supported yet, use reasoning.effort",
                requestJson);
        }

        var flatEffort = NormalizeOrNull(reasoning.ReasoningEffort);
        var nestedEffort = NormalizeOrNull(reasoning.ReasoningObjectEffort);

        if (flatEffort is not null && nestedEffort is not null && !string.Equals(flatEffort, nestedEffort, StringComparison.Ordinal))
        {
            return ReasoningPatchResult.Error(
                ErrorCodes.ConflictingParameters,
                "reasoning_effort and reasoning.effort must match",
                requestJson);
        }

        var selectedEffort = flatEffort ?? nestedEffort;
        if (selectedEffort is null)
        {
            // reasoning object/field present but no effort value — fall back to default if any.
            if (supportsReasoning && normalizedDefault is not null)
            {
                return InjectEffort(requestJson, normalizedDefault);
            }
            return ReasoningPatchResult.Success(requestJson);
        }

        if (!supportsReasoning)
        {
            return ReasoningPatchResult.Error(
                ErrorCodes.UnsupportedReasoning,
                "Model does not support reasoning",
                requestJson);
        }

        // Case 2: client explicitly sent an effort that is not in the allowed set.
        // If a default is configured, silently substitute it (per product decision); otherwise
        // return the original strict error so clients learn the allowed vocabulary.
        if (!normalizedAllowed.Contains(selectedEffort, StringComparer.Ordinal))
        {
            if (normalizedDefault is not null)
            {
                return InjectEffort(requestJson, normalizedDefault);
            }

            var allowedList = normalizedAllowed.Count == 0
                ? "none configured"
                : string.Join(", ", normalizedAllowed);

            return ReasoningPatchResult.Error(
                ErrorCodes.UnsupportedReasoningEffort,
                $"Model supports reasoning efforts: {allowedList}",
                requestJson);
        }

        return InjectEffort(requestJson, selectedEffort);
    }

    /// <summary>
    /// Normalizes reasoning on an already-parsed <paramref name="node"/>, mutating it in place.
    /// This avoids redundant parse/serialize cycles when chained with other patchers.
    /// </summary>
    public static ReasoningPatchResult TryNormalizeInPlace(
        JsonNode node,
        ChatCompletionReasoningMetadata reasoning,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts,
        string? defaultEffort = null)
    {
        var normalizedAllowed = NormalizeAllowedEfforts(allowedEfforts);
        var normalizedDefault = NormalizeOrNull(defaultEffort);
        if (normalizedDefault is not null && normalizedAllowed.Count > 0
            && !normalizedAllowed.Contains(normalizedDefault, StringComparer.Ordinal))
        {
            normalizedDefault = null;
        }

        if (!reasoning.HasReasoningEffort && !reasoning.HasReasoningObject)
        {
            if (supportsReasoning && normalizedDefault is not null)
            {
                InjectEffortInPlace(node, normalizedDefault);
            }
            return ReasoningPatchResult.InPlaceSuccess();
        }

        if (reasoning.HasUnsupportedMaxTokens || reasoning.HasUnsupportedEnabled)
        {
            return ReasoningPatchResult.Error(
                ErrorCodes.UnsupportedReasoning,
                "reasoning.max_tokens/enabled not supported yet, use reasoning.effort");
        }

        var flatEffort = NormalizeOrNull(reasoning.ReasoningEffort);
        var nestedEffort = NormalizeOrNull(reasoning.ReasoningObjectEffort);

        if (flatEffort is not null && nestedEffort is not null && !string.Equals(flatEffort, nestedEffort, StringComparison.Ordinal))
        {
            return ReasoningPatchResult.Error(
                ErrorCodes.ConflictingParameters,
                "reasoning_effort and reasoning.effort must match");
        }

        var selectedEffort = flatEffort ?? nestedEffort;
        if (selectedEffort is null)
        {
            if (supportsReasoning && normalizedDefault is not null)
            {
                InjectEffortInPlace(node, normalizedDefault);
            }
            return ReasoningPatchResult.InPlaceSuccess();
        }

        if (!supportsReasoning)
        {
            return ReasoningPatchResult.Error(
                ErrorCodes.UnsupportedReasoning,
                "Model does not support reasoning");
        }

        if (!normalizedAllowed.Contains(selectedEffort, StringComparer.Ordinal))
        {
            if (normalizedDefault is not null)
            {
                InjectEffortInPlace(node, normalizedDefault);
                return ReasoningPatchResult.InPlaceSuccess();
            }

            var allowedList = normalizedAllowed.Count == 0
                ? "none configured"
                : string.Join(", ", normalizedAllowed);

            return ReasoningPatchResult.Error(
                ErrorCodes.UnsupportedReasoningEffort,
                $"Model supports reasoning efforts: {allowedList}");
        }

        InjectEffortInPlace(node, selectedEffort);
        return ReasoningPatchResult.InPlaceSuccess();
    }

    private static ReasoningPatchResult InjectEffort(string requestJson, string effort)
    {
        try
        {
            var node = JsonNode.Parse(requestJson);
            if (node is not JsonObject obj)
            {
                return ReasoningPatchResult.Success(requestJson);
            }

            obj.Remove("reasoning");
            obj["reasoning_effort"] = effort;
            return ReasoningPatchResult.Success(obj.ToJsonString());
        }
        catch (JsonException)
        {
            return ReasoningPatchResult.Success(requestJson);
        }
    }

    private static void InjectEffortInPlace(JsonNode node, string effort)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("reasoning");
            obj["reasoning_effort"] = effort;
        }
    }

    private static string? NormalizeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();

    private static IReadOnlyList<string> NormalizeAllowedEfforts(IReadOnlyList<string> allowedEfforts)
    {
        if (allowedEfforts.Count == 0)
        {
            return [];
        }

        return allowedEfforts
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record ReasoningPatchResult(
    bool IsSuccess,
    string? PatchedJson,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static ReasoningPatchResult Success(string patchedJson) => new(true, patchedJson);

    public static ReasoningPatchResult InPlaceSuccess() => new(true, null);

    public static ReasoningPatchResult Error(string errorCode, string errorMessage, string? requestJson = null) =>
        new(false, requestJson, errorCode, errorMessage);
}
