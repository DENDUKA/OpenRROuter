using System.Text.Json;

namespace OpenRussiaRouter.BuildingBlocks.Usage;

public static class TokenUsageExtractor
{
    public static bool TryExtractJson(string json, out TokenUsage usage)
    {
        usage = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            // A chunk may carry `"usage": null` (e.g. MiniMax-M3 per-delta chunks).
            // TryGetProperty then returns true with a Null-kind element, and calling
            // TryGetProperty on that element throws InvalidOperationException — which is
            // NOT a JsonException, so it escapes the catch below and bubbles up as a 500.
            // Require an actual object before reading usage fields.
            if (!document.RootElement.TryGetProperty("usage", out var usageElement) ||
                usageElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // 9RFork inflates standard usage fields by BUFFER_TOKENS (~2000) for CLI
            // context tracking. Real counts are duplicated under `_rwt_raw` for billing.
            if (usageElement.TryGetProperty("_rwt_raw", out var rawElement) &&
                rawElement.ValueKind == JsonValueKind.Object)
            {
                usage = BuildUsage(
                    ReadInt64(rawElement, "prompt_tokens", "input_tokens"),
                    ReadInt64(rawElement, "completion_tokens", "output_tokens"),
                    usageElement);
                return true;
            }

            usage = BuildUsage(
                ReadInt64(usageElement, "input_tokens", "prompt_tokens"),
                ReadInt64(usageElement, "output_tokens", "completion_tokens"),
                usageElement);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryExtractStreamingSse(string body, out TokenUsage usage)
    {
        usage = default;
        var found = false;

        using var reader = new StringReader(body);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            var line = rawLine.AsSpan().Trim();
            if (!line.StartsWith("data:".AsSpan(), StringComparison.Ordinal))
                continue;

            var data = line["data:".Length..].TrimStart();
            if (data.SequenceEqual("[DONE]".AsSpan()))
                continue;

            if (TryExtractJson(data.ToString(), out var chunkUsage) && chunkUsage.TotalTokens > 0)
            {
                usage = chunkUsage;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Builds a <see cref="TokenUsage"/> enforcing the invariant that
    /// <see cref="TokenUsage.InputTokens"/> includes <see cref="TokenUsage.CacheTokens"/>
    /// (cached tokens are a subset of the prompt). OpenAI-style usage already satisfies
    /// this (<c>prompt_tokens</c> includes <c>cached_tokens</c>). Anthropic-style usage
    /// reports cache reads separately from <c>input_tokens</c>, so cache is added to input.
    /// </summary>
    private static TokenUsage BuildUsage(long inputTokens, long outputTokens, JsonElement usageElement)
    {
        var cacheTokens = ReadCacheTokens(usageElement, out var separateFromInput);
        if (separateFromInput)
        {
            inputTokens += cacheTokens;
        }

        return new TokenUsage(inputTokens, outputTokens, cacheTokens);
    }

    private static long ReadCacheTokens(JsonElement usageElement, out bool separateFromInput)
    {
        // Anthropic reports cache reads/creation separately from input_tokens.
        var anthropic = ReadInt64(usageElement, "cache_read_input_tokens", "cache_creation_input_tokens");
        if (anthropic > 0)
        {
            separateFromInput = true;
            return anthropic;
        }

        separateFromInput = false;

        // OpenAI-style: cached tokens are already part of prompt_tokens.
        var direct = ReadInt64(usageElement, "cache_tokens", "cached_tokens");
        if (direct > 0)
        {
            return direct;
        }

        return ReadNestedInt64(usageElement, "prompt_tokens_details", "cached_tokens")
            ?? ReadNestedInt64(usageElement, "input_tokens_details", "cached_tokens")
            ?? 0;
    }

    private static long? ReadNestedInt64(JsonElement element, string objectName, string propertyName)
    {
        if (element.TryGetProperty(objectName, out var nested) &&
            nested.ValueKind == JsonValueKind.Object &&
            nested.TryGetProperty(propertyName, out var property) &&
            property.TryGetInt64(out var value))
        {
            return value;
        }

        return null;
    }

    private static long ReadInt64(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.TryGetInt64(out var value))
            {
                return value;
            }
        }

        return 0;
    }
}
