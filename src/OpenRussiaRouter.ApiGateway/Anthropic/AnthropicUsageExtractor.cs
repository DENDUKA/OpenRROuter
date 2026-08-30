using System.Text.Json;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Anthropic;

public static class AnthropicUsageExtractor
{
    public static bool TryExtract(string json, out TokenUsage usage)
    {
        usage = default;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 1. Check root or message for usage object
            JsonElement usageElem;
            if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                usageElem = u;
            }
            else if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object &&
                     m.TryGetProperty("usage", out var mu) && mu.ValueKind == JsonValueKind.Object)
            {
                usageElem = mu;
            }
            else
            {
                return false;
            }

            // 2. Check for _orr_raw first
            if (usageElem.TryGetProperty("_orr_raw", out var rawElem) && rawElem.ValueKind == JsonValueKind.Object)
            {
                var rawInput = ReadInt64(rawElem, "prompt_tokens", "input_tokens");
                var rawOutput = ReadInt64(rawElem, "completion_tokens", "output_tokens");
                var rawCache = ReadInt64(rawElem, "cache_read_input_tokens", "cache_tokens", "cached_tokens");

                if (rawElem.TryGetProperty("cache_creation_input_tokens", out var rawCreation) && rawCreation.TryGetInt64(out var cc))
                {
                    rawInput += (rawCache + cc);
                }
                else if (rawCache > 0 && !rawElem.TryGetProperty("prompt_tokens", out _))
                {
                    rawInput += rawCache;
                }

                usage = new TokenUsage(rawInput, rawOutput, rawCache);
                return usage.TotalTokens > 0;
            }

            // 3. Anthropic standard usage format
            var inputTokens = ReadInt64(usageElem, "input_tokens", "prompt_tokens");
            var outputTokens = ReadInt64(usageElem, "output_tokens", "completion_tokens");
            var cacheRead = ReadInt64(usageElem, "cache_read_input_tokens");
            var cacheCreation = ReadInt64(usageElem, "cache_creation_input_tokens");

            long cacheTokens = 0;
            if (cacheRead > 0 || cacheCreation > 0)
            {
                cacheTokens = cacheRead;
                inputTokens += (cacheRead + cacheCreation);
            }
            else
            {
                cacheTokens = ReadInt64(usageElem, "cache_tokens", "cached_tokens");
            }

            usage = new TokenUsage(inputTokens, outputTokens, cacheTokens);
            return usage.TotalTokens > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static long ReadInt64(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.TryGetInt64(out var val))
            {
                return val;
            }
        }
        return 0;
    }
}
