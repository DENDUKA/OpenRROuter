using System.Text.Json.Nodes;

namespace OpenRussiaRouter.ApiGateway.OpenAi;

public static class SseRequestPatcher
{
    /// <summary>
    /// Ensures "stream": true and "stream_options": {"include_usage": true} are present
    /// so 9Router returns token usage in the final SSE chunk.
    /// </summary>
    public static string InjectStreamOptions(string requestJson)
    {
        try
        {
            var node = JsonNode.Parse(requestJson);
            if (node is JsonObject obj)
            {
                obj["stream"] = true;
                if (!obj.ContainsKey("stream_options"))
                    obj["stream_options"] = new JsonObject { ["include_usage"] = true };
                return obj.ToJsonString();
            }
        }
        catch { /* malformed JSON — return as-is */ }
        return requestJson;
    }
}
