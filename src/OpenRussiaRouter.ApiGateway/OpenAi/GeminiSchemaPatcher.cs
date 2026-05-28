using System.Text.Json.Nodes;

namespace OpenRussiaRouter.ApiGateway.OpenAi;

public static class GeminiSchemaPatcher
{
    public static string SanitizeIfGeminiModel(string requestJson, string routerModelId)
    {
        if (!IsGeminiModel(routerModelId))
            return requestJson;

        try
        {
            var node = JsonNode.Parse(requestJson);
            if (node is not JsonObject obj)
                return requestJson;

            return SanitizeKnownSchemaRoots(obj) ? obj.ToJsonString() : requestJson;
        }
        catch
        {
            return requestJson;
        }
    }

    private static bool IsGeminiModel(string routerModelId)
    {
        return routerModelId.Contains("gemini", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SanitizeKnownSchemaRoots(JsonObject request)
    {
        var changed = false;

        if (request["tools"] is JsonArray tools)
        {
            foreach (var tool in tools.OfType<JsonObject>())
            {
                if (tool["function"] is JsonObject function &&
                    function["parameters"] is JsonNode parameters)
                {
                    changed |= SanitizeSchemaNode(parameters);
                }
            }
        }

        if (request["functions"] is JsonArray functions)
        {
            foreach (var function in functions.OfType<JsonObject>())
            {
                if (function["parameters"] is JsonNode parameters)
                    changed |= SanitizeSchemaNode(parameters);
            }
        }

        if (request["response_format"] is JsonObject responseFormat &&
            responseFormat["json_schema"] is JsonObject jsonSchema &&
            jsonSchema["schema"] is JsonNode responseSchema)
        {
            changed |= SanitizeSchemaNode(responseSchema);
        }

        return changed;
    }

    private static bool SanitizeSchemaNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var refKey = obj.ContainsKey("$ref")
                ? "$ref"
                : obj.ContainsKey("ref")
                    ? "ref"
                    : null;

            if (refKey is not null)
                return RemoveRefSiblings(obj, refKey);

            var changed = false;
            foreach (var child in obj.ToList())
            {
                changed |= SanitizeSchemaNode(child.Value);
            }

            return changed;
        }

        if (node is JsonArray array)
        {
            var changed = false;
            foreach (var item in array)
            {
                changed |= SanitizeSchemaNode(item);
            }

            return changed;
        }

        return false;
    }

    private static bool RemoveRefSiblings(JsonObject obj, string refKey)
    {
        var changed = false;
        foreach (var key in obj.Select(property => property.Key).Where(key => key != refKey).ToArray())
        {
            obj.Remove(key);
            changed = true;
        }

        return changed;
    }
}
