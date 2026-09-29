using System.Text.Json.Nodes;

namespace OpenRussiaRouter.ApiGateway.Billing;

/// <summary>
/// T-070: pre-authorizes a paid request against the remaining balance before it reaches a node.
/// Billing charges only after the response, so without this a request started on a tiny positive
/// balance could cost far more than the balance. The guard estimates the input cost from the body
/// and rejects requests the balance cannot cover; otherwise it lowers the output token limit so
/// the provider itself stops generating where the money ends (the stream finishes normally with
/// <c>finish_reason: length</c> and real usage — the gateway never cuts a stream).
/// </summary>
public static class SpendBudgetGuard
{
    /// <summary>Conservative chars-per-token ratio: overestimates English, roughly right for code/Cyrillic.</summary>
    internal const int CharsPerToken = 3;

    /// <summary>Below this many affordable output tokens the request is rejected instead of truncated.</summary>
    internal const int MinOutputTokens = 256;

    /// <summary>
    /// A limit is injected into a body without one only up to this value: every chat model accepts it,
    /// while a larger value may exceed a model's max output and turn into a provider 400.
    /// </summary>
    internal const int SafeOutputCeiling = 4096;

    /// <summary>Anthropic's minimum extended-thinking budget.</summary>
    private const int MinThinkingBudget = 1024;

    private static readonly string[] OpenAiLimitFields = ["max_completion_tokens", "max_tokens"];

    public static SpendBudgetDecision Apply(
        JsonNode? body,
        bool isAnthropic,
        decimal balanceUsd,
        decimal inputPricePerMillionUsd,
        decimal outputPricePerMillionUsd)
    {
        // Unknown prices (older Billing) or a free model: nothing to protect.
        if (body is not JsonObject obj || (inputPricePerMillionUsd <= 0m && outputPricePerMillionUsd <= 0m))
            return SpendBudgetDecision.Unchanged;

        var estimatedInputTokens = EstimateInputTokens(obj);
        // Cached input is billed cheaper, but the guard assumes the full input price on purpose.
        var remainingUsd = balanceUsd - estimatedInputTokens * inputPricePerMillionUsd / 1_000_000m;
        if (remainingUsd <= 0m)
            return SpendBudgetDecision.Reject(estimatedInputTokens);

        if (outputPricePerMillionUsd <= 0m)
            return SpendBudgetDecision.Unchanged;

        var affordable = decimal.Floor(remainingUsd * 1_000_000m / outputPricePerMillionUsd);
        var affordableTokens = affordable >= int.MaxValue ? int.MaxValue : (int)affordable;
        if (affordableTokens < MinOutputTokens)
            return SpendBudgetDecision.Reject(estimatedInputTokens);

        var limited = isAnthropic
            ? LimitAnthropic(obj, affordableTokens)
            : LimitOpenAi(obj, affordableTokens);

        return limited
            ? SpendBudgetDecision.Limit(estimatedInputTokens, affordableTokens)
            : SpendBudgetDecision.Unchanged;
    }

    internal static long EstimateInputTokens(JsonObject body)
    {
        var chars = CountChars(body);
        return (chars + CharsPerToken - 1) / CharsPerToken;
    }

    private static long CountChars(JsonNode? node, string? propertyName = null)
    {
        switch (node)
        {
            case JsonObject obj:
                long objectChars = 0;
                foreach (var (name, child) in obj)
                    objectChars += name.Length + CountChars(child, name);
                return objectChars;
            case JsonArray array:
                long arrayChars = 0;
                foreach (var item in array)
                    arrayChars += CountChars(item, propertyName);
                return arrayChars;
            case JsonValue value when value.TryGetValue<string>(out var text):
                // Base64 images/documents (Anthropic `source.data`, OpenAI `data:` URLs) are billed
                // per image, not per character — counting them would reject every image request.
                return IsBinaryPayload(propertyName, text) ? 0 : text.Length;
            default:
                return 0;
        }
    }

    private static bool IsBinaryPayload(string? propertyName, string text) =>
        string.Equals(propertyName, "data", StringComparison.Ordinal)
        || text.StartsWith("data:", StringComparison.Ordinal);

    private static bool LimitOpenAi(JsonObject obj, int affordableTokens)
    {
        var hasLimit = false;
        var limited = false;
        foreach (var field in OpenAiLimitFields)
        {
            if (!TryGetLimit(obj, field, out var requested))
                continue;

            hasLimit = true;
            if (requested > affordableTokens)
            {
                obj[field] = affordableTokens;
                limited = true;
            }
        }

        if (!hasLimit && affordableTokens <= SafeOutputCeiling)
        {
            obj["max_tokens"] = affordableTokens;
            limited = true;
        }

        return limited;
    }

    private static bool LimitAnthropic(JsonObject obj, int affordableTokens)
    {
        var hasLimit = TryGetLimit(obj, "max_tokens", out var requested);
        if (hasLimit ? requested <= affordableTokens : affordableTokens > SafeOutputCeiling)
            return false;

        obj["max_tokens"] = affordableTokens;

        // Anthropic requires thinking.budget_tokens < max_tokens, otherwise the request is a 400.
        if (obj["thinking"] is JsonObject thinking && TryGetLimit(thinking, "budget_tokens", out var budget)
            && budget >= affordableTokens)
        {
            if (affordableTokens > MinThinkingBudget)
                thinking["budget_tokens"] = affordableTokens - 1;
            else
                obj.Remove("thinking");
        }

        return true;
    }

    private static bool TryGetLimit(JsonObject obj, string field, out long value)
    {
        value = 0;
        return obj[field] is JsonValue node && node.TryGetValue(out value);
    }
}

/// <summary>Outcome of <see cref="SpendBudgetGuard.Apply"/>.</summary>
/// <param name="Rejected">The balance cannot cover the request — answer 402 before choosing a node.</param>
/// <param name="EstimatedInputTokens">Input tokens estimated from the body (0 when not evaluated).</param>
/// <param name="OutputLimit">Output token limit written into the body, or <c>null</c> if untouched.</param>
public readonly record struct SpendBudgetDecision(bool Rejected, long EstimatedInputTokens, int? OutputLimit)
{
    public static SpendBudgetDecision Unchanged => default;

    public static SpendBudgetDecision Reject(long estimatedInputTokens) => new(true, estimatedInputTokens, null);

    public static SpendBudgetDecision Limit(long estimatedInputTokens, int outputLimit) =>
        new(false, estimatedInputTokens, outputLimit);
}
