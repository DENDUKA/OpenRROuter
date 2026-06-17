using System.Collections.Concurrent;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Thread-safe in-memory store that maps each router node to its set of supported model IDs.
/// Populated by <see cref="CapabilityReplicaRefresher"/> on a periodic schedule.
/// </summary>
public sealed class NodeModelCapabilityStore
{
    private readonly object _lock = new();
    private Dictionary<string, IReadOnlySet<string>> _nodeModels = new(StringComparer.Ordinal);

    /// <summary>Returns true if no node model data has been collected yet (e.g. right after startup).</summary>
    public bool IsEmpty
    {
        get { lock (_lock) return _nodeModels.Count == 0; }
    }

    /// <summary>Updates the model list for the given node.</summary>
    public void SetNodeModels(string nodeId, IReadOnlySet<string> modelIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(modelIds);
        lock (_lock)
            _nodeModels[nodeId] = modelIds;
    }

    /// <summary>Returns true if the node has the requested model in its capability list.</summary>
    public bool NodeSupportsModel(string nodeId, string modelId)
    {
        lock (_lock)
            return _nodeModels.TryGetValue(nodeId, out var models) && models.Contains(modelId);
    }

    /// <summary>Atomically replaces the entire node→models map with the given snapshot.</summary>
    public void ReplaceAll(IReadOnlyDictionary<string, IReadOnlySet<string>> nodeModels)
    {
        ArgumentNullException.ThrowIfNull(nodeModels);
        lock (_lock)
            _nodeModels = new Dictionary<string, IReadOnlySet<string>>(nodeModels, StringComparer.Ordinal);
    }
}
