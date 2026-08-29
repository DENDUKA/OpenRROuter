namespace OpenRussiaRouter.CollectorService.Application.Collection;

/// <summary>Pure projections over a collected snapshot. No I/O.</summary>
public static class CatalogProjection
{
    /// <summary>node_id -> ordered set of routable model ids (public id, else upstream id).</summary>
    public static IReadOnlyList<(string NodeId, IReadOnlyList<string> ModelIds)> Capabilities(
        CollectorSnapshot snapshot)
    {
        var result = new List<(string, IReadOnlyList<string>)>();
        foreach (var node in snapshot.Nodes.Where(n => !n.Unreachable))
        {
            var modelIds = node.Observations
                .Select(GetRoutableModelId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            result.Add((node.NodeId, modelIds));
        }
        return result;
    }

    /// <summary>Routable model ids for a single node, or null if the node is absent/unreachable.</summary>
    public static IReadOnlyList<string>? NodeModels(CollectorSnapshot snapshot, string nodeId)
    {
        var node = snapshot.Nodes.FirstOrDefault(n => string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));
        if (node is null || node.Unreachable)
            return null;

        return node.Observations
            .Select(GetRoutableModelId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Merged rich catalog across all reachable nodes.</summary>
    public static IReadOnlyList<CatalogEntry> Catalog(CollectorSnapshot snapshot)
    {
        var accumulators = new Dictionary<string, CatalogAccumulator>(StringComparer.Ordinal);
        foreach (var result in snapshot.Nodes
                     .Where(n => !n.Unreachable)
                     .OrderBy(n => n.NodeId, StringComparer.Ordinal))
        {
            foreach (var observation in result.Observations)
            {
                if (!accumulators.TryGetValue(observation.UpstreamId, out var acc))
                {
                    acc = new CatalogAccumulator { UpstreamId = observation.UpstreamId };
                    accumulators[observation.UpstreamId] = acc;
                }

                (acc.PublicId, acc.PublicIdRank) =
                    ChooseField(acc.PublicId, acc.PublicIdRank, observation.PublicId, observation.Rank);
                (acc.DisplayName, acc.DisplayNameRank) =
                    ChooseField(acc.DisplayName, acc.DisplayNameRank, observation.DisplayName, observation.Rank);
                (acc.ModelType, acc.ModelTypeRank) =
                    ChooseField(acc.ModelType, acc.ModelTypeRank, observation.ModelType, observation.Rank);

                foreach (var source in observation.SourceProviders)
                    if (!string.IsNullOrWhiteSpace(source))
                        acc.SourceProviders.Add(source);

                acc.NodeIds.Add(result.NodeId);

                // 9Router publishes the complete effective capability.  In particular, a
                // combo's list is already the intersection of all its fallbacks.  Keep one
                // deterministic node snapshot instead of merging capabilities across nodes.
                if (!acc.HasReasoningDefinition || observation.Rank > acc.ReasoningDefinitionRank)
                {
                    acc.HasReasoningDefinition = true;
                    acc.ReasoningDefinitionRank = observation.Rank;
                    acc.SupportsReasoning = observation.SupportsReasoning && observation.ReasoningEfforts.Count > 0;
                    acc.ThinkingFormat = observation.ThinkingFormat;
                    acc.ThinkingCanDisable = observation.ThinkingCanDisable;
                    acc.ThinkingOptions = observation.ThinkingOptions.ToList();
                    acc.ReasoningEfforts = observation.ReasoningEfforts.ToList();
                    acc.ComboReasoningMembers = observation.ComboReasoningMembers.ToList();
                }
            }
        }

        return accumulators.Values
            .OrderBy(a => a.UpstreamId, StringComparer.Ordinal)
            .Select(acc => new CatalogEntry(
                acc.UpstreamId,
                string.IsNullOrWhiteSpace(acc.PublicId) ? acc.UpstreamId : acc.PublicId,
                acc.DisplayName,
                acc.ModelType,
                acc.SourceProviders.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                acc.NodeIds.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                acc.SupportsReasoning,
                acc.ThinkingFormat,
                acc.ThinkingCanDisable,
                acc.ThinkingOptions,
                acc.ReasoningEfforts,
                acc.ComboReasoningMembers))
            .ToList();
    }

    private static string GetRoutableModelId(ModelObservation o) =>
        string.IsNullOrWhiteSpace(o.PublicId) ? o.UpstreamId : o.PublicId;

    private static (string Value, int Rank) ChooseField(string current, int currentRank, string incoming, int incomingRank)
    {
        if (string.IsNullOrWhiteSpace(incoming))
            return (current, currentRank);
        if (incomingRank > currentRank || (incomingRank == currentRank && string.IsNullOrWhiteSpace(current)))
            return (incoming, incomingRank);
        return (current, currentRank);
    }

    private sealed class CatalogAccumulator
    {
        public string UpstreamId { get; init; } = string.Empty;
        public string PublicId { get; set; } = string.Empty;
        public int PublicIdRank { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int DisplayNameRank { get; set; }
        public string ModelType { get; set; } = string.Empty;
        public int ModelTypeRank { get; set; }
        public HashSet<string> SourceProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool SupportsReasoning { get; set; }
        public bool HasReasoningDefinition { get; set; }
        public int ReasoningDefinitionRank { get; set; }
        public string? ThinkingFormat { get; set; }
        public bool ThinkingCanDisable { get; set; } = true;
        public List<string> ThinkingOptions { get; set; } = [];
        public List<string> ReasoningEfforts { get; set; } = [];
        public List<ComboReasoningMember> ComboReasoningMembers { get; set; } = [];
    }
}

public sealed record CatalogEntry(
    string UpstreamId,
    string PublicId,
    string DisplayName,
    string ModelType,
    IReadOnlyList<string> SourceProviders,
    IReadOnlyList<string> NodeIds,
    bool SupportsReasoning,
    string? ThinkingFormat,
    bool ThinkingCanDisable,
    IReadOnlyList<string> ThinkingOptions,
    IReadOnlyList<string> ReasoningEfforts,
    IReadOnlyList<ComboReasoningMember> ComboReasoningMembers);
