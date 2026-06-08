using System.Text.Json;
using System.Text.Json.Nodes;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.ApiGateway.OpenAi;

public static class ReasoningRequestPatcher
{
    public static ReasoningPatchResult TryNormalize(
        string requestJson,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts)
    {
        return TryNormalize(
            requestJson,
            ChatCompletionRequestReader.Read(requestJson).Reasoning,
            supportsReasoning,
            allowedEfforts);
    }

    public static ReasoningPatchResult TryNormalize(
        string requestJson,
        ChatCompletionReasoningMetadata reasoning,
        bool supportsReasoning,
        IReadOnlyList<string> allowedEfforts)
    {
        if (!reasoning.HasReasoningEffort && !reasoning.HasReasoningObject)
        {
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
            return ReasoningPatchResult.Success(requestJson);
        }

        if (!supportsReasoning)
        {
            return ReasoningPatchResult.Error(
                ErrorCodes.UnsupportedReasoning,
                "Model does not support reasoning",
                requestJson);
        }

        var normalizedAllowedEfforts = NormalizeAllowedEfforts(allowedEfforts);
        if (!normalizedAllowedEfforts.Contains(selectedEffort, StringComparer.Ordinal))
        {
            var allowedList = normalizedAllowedEfforts.Count == 0
                ? "none configured"
                : string.Join(", ", normalizedAllowedEfforts);

            return ReasoningPatchResult.Error(
                ErrorCodes.UnsupportedReasoningEffort,
                $"Model supports reasoning efforts: {allowedList}",
                requestJson);
        }

        try
        {
            var node = JsonNode.Parse(requestJson);
            if (node is not JsonObject obj)
            {
                return ReasoningPatchResult.Success(requestJson);
            }

            obj.Remove("reasoning");
            obj["reasoning_effort"] = selectedEffort;
            return ReasoningPatchResult.Success(obj.ToJsonString());
        }
        catch (JsonException)
        {
            return ReasoningPatchResult.Success(requestJson);
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
    string PatchedJson,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static ReasoningPatchResult Success(string patchedJson) => new(true, patchedJson);

    public static ReasoningPatchResult Error(string errorCode, string errorMessage, string requestJson) =>
        new(false, requestJson, errorCode, errorMessage);
}
