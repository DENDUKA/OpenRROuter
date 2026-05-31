using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenRussiaRouter.ApiGateway.OpenAi;

public static class SseRequestPatcher
{
    /// <summary>
    /// Replaces the "model" field value in the request JSON with <paramref name="routerModelId"/>.
    /// Used to translate the billing model_id to the actual upstream model ID before forwarding.
    /// </summary>
    public static string PatchModel(string requestJson, string routerModelId)
    {
        try
        {
            var node = JsonNode.Parse(requestJson);
            if (node is JsonObject obj)
            {
                obj["model"] = routerModelId;
                return obj.ToJsonString();
            }
        }
        catch (JsonException) { /* malformed JSON — return as-is */ }
        return requestJson;
    }

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
        catch (JsonException) { /* malformed JSON — return as-is */ }
        return requestJson;
    }
}
