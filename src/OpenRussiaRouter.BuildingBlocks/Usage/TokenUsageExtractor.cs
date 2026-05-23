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
            if (!document.RootElement.TryGetProperty("usage", out var usageElement))
            {
                return false;
            }

            // 9RFork inflates standard usage fields by BUFFER_TOKENS (~2000) for CLI
            // context tracking. Real counts are duplicated under `_orr_raw` for billing.
            if (usageElement.TryGetProperty("_orr_raw", out var rawElement) &&
                rawElement.ValueKind == JsonValueKind.Object)
            {
                usage = new TokenUsage(
                    ReadInt64(rawElement, "prompt_tokens", "input_tokens"),
                    ReadInt64(rawElement, "completion_tokens", "output_tokens"),
                    ReadCacheTokens(usageElement));
                return true;
            }

            usage = new TokenUsage(
                ReadInt64(usageElement, "input_tokens", "prompt_tokens"),
                ReadInt64(usageElement, "output_tokens", "completion_tokens"),
                ReadCacheTokens(usageElement));
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

        foreach (var rawLine in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].TrimStart();
            if (data == "[DONE]")
            {
                continue;
            }

            if (TryExtractJson(data, out var chunkUsage) && chunkUsage.TotalTokens > 0)
            {
                usage = chunkUsage;
                found = true;
            }
        }

        return found;
    }

    private static long ReadCacheTokens(JsonElement usageElement)
    {
        var direct = ReadInt64(usageElement, "cache_tokens", "cached_tokens", "cache_read_input_tokens");
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
