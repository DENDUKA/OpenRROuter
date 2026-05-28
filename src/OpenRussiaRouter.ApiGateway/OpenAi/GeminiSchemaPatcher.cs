using System.Text.Json.Nodes;

namespace OpenRussiaRouter.ApiGateway.OpenAi;

public static class GeminiSchemaPatcher
{
    private static readonly HashSet<string> SchemaPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "parameters",
        "schema",
        "input_schema",
        "inputSchema",
        "parameters_json_schema",
        "parametersJsonSchema"
    };

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
        var requestDefinitions = CollectDefinitions(request);

        changed |= SanitizeSchemaBranch(request["tools"], requestDefinitions);
        changed |= SanitizeSchemaBranch(request["functions"], requestDefinitions);
        changed |= SanitizeSchemaBranch(request["response_format"], requestDefinitions);
        changed |= SanitizeSchemaRootsByName(request);

        return changed;
    }

    private static bool SanitizeSchemaBranch(JsonNode? node, IReadOnlyDictionary<string, JsonNode> requestDefinitions)
    {
        if (node is null)
            return false;

        var branchDefinitions = new Dictionary<string, JsonNode>(requestDefinitions, StringComparer.Ordinal);
        foreach (var definition in CollectDefinitions(node))
        {
            branchDefinitions.TryAdd(definition.Key, definition.Value.DeepClone());
        }

        var changed = SanitizeSchemaNode(node, branchDefinitions, new HashSet<string>(StringComparer.Ordinal), depth: 0);
        changed |= RemoveDefinitionBags(node);
        changed |= RemoveRemainingRefs(node);

        return changed;
    }

    private static bool RemoveRemainingRefs(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var changed = false;
            var refKey = FindRefKey(obj);
            if (refKey is not null)
            {
                obj.Remove(refKey);
                changed = true;

                if (obj.Count == 0)
                    obj["type"] = "object";
            }

            foreach (var child in obj.ToList())
            {
                changed |= RemoveRemainingRefs(child.Value);
            }

            return changed;
        }

        if (node is JsonArray array)
        {
            var changed = false;
            foreach (var item in array)
            {
                changed |= RemoveRemainingRefs(item);
            }

            return changed;
        }

        return false;
    }

    private static bool RemoveDefinitionBags(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var changed = false;
            changed |= obj.Remove("$defs");
            changed |= obj.Remove("defs");
            changed |= obj.Remove("definitions");

            foreach (var child in obj.ToList())
            {
                changed |= RemoveDefinitionBags(child.Value);
            }

            return changed;
        }

        if (node is JsonArray array)
        {
            var changed = false;
            foreach (var item in array)
            {
                changed |= RemoveDefinitionBags(item);
            }

            return changed;
        }

        return false;
    }

    private static bool SanitizeSchemaRootsByName(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var changed = false;

            foreach (var property in obj.ToList())
            {
                if (property.Value is null)
                    continue;

                changed |= SchemaPropertyNames.Contains(property.Key)
                    ? SanitizeSchemaRoot(property.Value)
                    : SanitizeSchemaRootsByName(property.Value);
            }

            return changed;
        }

        if (node is JsonArray array)
        {
            var changed = false;
            foreach (var item in array)
            {
                changed |= SanitizeSchemaRootsByName(item);
            }

            return changed;
        }

        return false;
    }

    private static bool SanitizeSchemaRoot(JsonNode schema)
    {
        if (schema is not JsonObject root)
            return SanitizeSchemaNode(schema, new Dictionary<string, JsonNode>(), new HashSet<string>(StringComparer.Ordinal), depth: 0);

        var definitions = CollectDefinitions(root);
        var changed = SanitizeSchemaNode(root, definitions, new HashSet<string>(StringComparer.Ordinal), depth: 0);

        changed |= root.Remove("$defs");
        changed |= root.Remove("defs");
        changed |= root.Remove("definitions");

        return changed;
    }

    private static Dictionary<string, JsonNode> CollectDefinitions(JsonNode root)
    {
        var definitions = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        CollectDefinitions(root, definitions);
        return definitions;
    }

    private static void CollectDefinitions(JsonNode? node, Dictionary<string, JsonNode> definitions)
    {
        if (node is JsonObject obj)
        {
            AddDefinitions(obj["$defs"], definitions);
            AddDefinitions(obj["defs"], definitions);
            AddDefinitions(obj["definitions"], definitions);

            foreach (var child in obj)
            {
                if (child.Key is "$defs" or "defs" or "definitions")
                    continue;

                CollectDefinitions(child.Value, definitions);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                CollectDefinitions(item, definitions);
            }
        }
    }

    private static void AddDefinitions(JsonNode? node, Dictionary<string, JsonNode> definitions)
    {
        if (node is not JsonObject obj)
            return;

        foreach (var definition in obj)
        {
            if (definition.Value is not null)
                definitions.TryAdd(definition.Key, definition.Value.DeepClone());
        }
    }

    private static bool SanitizeSchemaNode(
        JsonNode? node,
        IReadOnlyDictionary<string, JsonNode> definitions,
        HashSet<string> resolving,
        int depth)
    {
        if (node is JsonObject obj)
        {
            var refKey = FindRefKey(obj);

            if (refKey is not null)
                return InlineRef(obj, refKey, definitions, resolving, depth);

            var changed = false;
            foreach (var child in obj.ToList())
            {
                changed |= SanitizeSchemaNode(child.Value, definitions, resolving, depth);
            }

            return changed;
        }

        if (node is JsonArray array)
        {
            var changed = false;
            foreach (var item in array)
            {
                changed |= SanitizeSchemaNode(item, definitions, resolving, depth);
            }

            return changed;
        }

        return false;
    }

    private static bool InlineRef(
        JsonObject obj,
        string refKey,
        IReadOnlyDictionary<string, JsonNode> definitions,
        HashSet<string> resolving,
        int depth)
    {
        var refName = ReadRefName(obj[refKey]);
        if (refName is null ||
            depth > 32 ||
            resolving.Contains(refName) ||
            !definitions.TryGetValue(refName, out var definition) ||
            definition.DeepClone() is not JsonObject inlined)
        {
            obj.Remove(refKey);
            if (obj.Count == 0)
                obj["type"] = "object";
            return true;
        }

        var siblings = obj
            .Where(property => property.Key != refKey)
            .Select(property => new KeyValuePair<string, JsonNode?>(property.Key, property.Value?.DeepClone()))
            .ToArray();

        foreach (var sibling in siblings)
        {
            inlined[sibling.Key] = sibling.Value;
        }

        foreach (var key in obj.Select(property => property.Key).ToArray())
        {
            obj.Remove(key);
        }

        foreach (var property in inlined.ToArray())
        {
            obj[property.Key] = property.Value?.DeepClone();
        }

        resolving.Add(refName);
        SanitizeSchemaNode(obj, definitions, resolving, depth + 1);
        resolving.Remove(refName);

        return true;
    }

    private static string? ReadRefName(JsonNode? node)
    {
        string? value;
        try
        {
            value = node?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
            return null;

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var name = segments.Length == 0 ? value : segments[^1];

        return name.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    }

    private static string? FindRefKey(JsonObject obj)
    {
        if (obj.ContainsKey("$ref") && ReadRefName(obj["$ref"]) is not null)
            return "$ref";

        if (obj.ContainsKey("ref") && ReadRefName(obj["ref"]) is not null)
            return "ref";

        return null;
    }
}
