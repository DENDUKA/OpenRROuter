using System.Text.Json;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Router;

/// <summary>
/// Extracts token usage from a 9Router/OpenAI-compatible response body.
/// </summary>
public static class RouterUsageExtractor
{
    /// <summary>
    /// Attempts to extract usage from JSON response body.
    /// </summary>
    /// <param name="json">Router response JSON.</param>
    /// <param name="usage">Extracted usage.</param>
    /// <returns>Whether usage was found.</returns>
    public static bool TryExtract(string json, out TokenUsage usage)
    {
        usage = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("usage", out var usageElement))
        {
            return false;
        }

        usage = new TokenUsage(
            ReadInt64(usageElement, "input_tokens", "prompt_tokens"),
            ReadInt64(usageElement, "output_tokens", "completion_tokens"),
            ReadInt64(usageElement, "cache_tokens", "cached_tokens"));
        return true;
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

