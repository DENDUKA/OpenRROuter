using System.Text.Json;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Router;

public static class RouterUsageExtractor
{

    public static bool TryExtract(string json, out TokenUsage usage)
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

