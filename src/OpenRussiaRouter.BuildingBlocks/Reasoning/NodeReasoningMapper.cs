namespace OpenRussiaRouter.BuildingBlocks.Reasoning;

/// <summary>
/// Maps 9Router node-side reasoning options (<c>caps.thinkingOptions</c> from
/// <c>/api/integrations/providers/with-models</c>) into ORR's canonical reasoning
/// effort vocabulary (<c>none,minimal,low,medium,high,xhigh,max,ultra,thinking</c>).
/// </summary>
/// <remarks>
/// 9Router emits two option vocabularies depending on the model's
/// <c>thinkingFormat</c> (see <c>F9Router/src/shared/constants/providers.js</c>):
/// <list type="bullet">
/// <item><c>extended</c> family (claude-adaptive, gemini-level): <c>["auto","on","off"]</c></item>
/// <item><c>effort</c> family (everything else): <c>["auto","none","low","medium","high"]</c></item>
/// </list>
/// Neither <c>auto</c> nor <c>on</c>/<c>off</c> are valid ORR canonical tokens, so they are
/// translated here so callers can write the result straight into
/// <c>allowed_reasoning_efforts</c> without tripping <c>BillingApplicationService</c>
/// validation. <c>auto</c> is dropped (no canonical equivalent — it means "let the
/// model decide"); <c>on</c>→<c>high</c>; <c>off</c>→<c>none</c>.
/// </remarks>
public static class NodeReasoningMapper
{
    /// <summary>Canonical ORR efforts in display order (low → high).</summary>
    public static readonly string[] OrderedCanonical = ["none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra", "thinking"];

    private static readonly HashSet<string> CanonicalSet = new(OrderedCanonical, StringComparer.Ordinal);

    /// <summary>
    /// Translates a 9Router thinking-options list into the canonical ORR effort set,
    /// deduplicated and ordered low → high. Returns an empty list when nothing maps
    /// (e.g. the node only advertised <c>["auto"]</c>).
    /// </summary>
    public static IReadOnlyList<string> MapFromNodeOptions(IReadOnlyList<string>? nodeOptions)
    {
        if (nodeOptions is null || nodeOptions.Count == 0)
        {
            return [];
        }

        var translated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in nodeOptions)
        {
            var mapped = TranslateToken(raw);
            if (mapped is not null)
            {
                translated.Add(mapped);
            }
        }

        if (translated.Count == 0)
        {
            return [];
        }

        // Preserve canonical order for deterministic downstream rendering.
        var result = new List<string>(translated.Count);
        foreach (var canonical in OrderedCanonical)
        {
            if (translated.Contains(canonical))
            {
                result.Add(canonical);
            }
        }
        return result;
    }

    /// <summary>
    /// Picks a sensible default effort from an allowed set, preferring
    /// <c>medium</c> &gt; <c>high</c> &gt; <c>low</c> &gt; first available.
    /// Returns <c>null</c> only when the allowed set is empty.
    /// </summary>
    public static string? PickDefaultEffort(IReadOnlyList<string>? allowed)
    {
        if (allowed is null || allowed.Count == 0)
        {
            return null;
        }

        foreach (var preferred in new[] { "medium", "high", "low" })
        {
            for (var i = 0; i < allowed.Count; i++)
            {
                if (string.Equals(allowed[i], preferred, StringComparison.Ordinal))
                {
                    return preferred;
                }
            }
        }

        return allowed[0];
    }

    private static string? TranslateToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var v = raw.Trim().ToLowerInvariant();
        return v switch
        {
            "on" => "high",
            "off" => "none",
            _ when CanonicalSet.Contains(v) => v,
            _ => null
        };
    }
}
