using System.Collections.Concurrent;
using System.Threading;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Thread-safe in-memory store that maps each router node to its set of supported model IDs.
/// Populated by <see cref="CapabilityReplicaRefresher"/> on a periodic schedule.
/// </summary>
public sealed class NodeModelCapabilityStore
{
    private ConcurrentDictionary<string, IReadOnlySet<string>> _nodeModels = new(StringComparer.Ordinal);

    /// <summary>Returns true if no node model data has been collected yet (e.g. right after startup).</summary>
    public bool IsEmpty => Volatile.Read(ref _nodeModels).IsEmpty;

    /// <summary>Updates the model list for the given node.</summary>
    public void SetNodeModels(string nodeId, IReadOnlySet<string> modelIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(modelIds);
        Volatile.Read(ref _nodeModels)[nodeId] = modelIds;
    }

    /// <summary>Returns true if the node has the requested model in its capability list.</summary>
    public bool NodeSupportsModel(string nodeId, string modelId)
        => Volatile.Read(ref _nodeModels).TryGetValue(nodeId, out var models)
           && models.Contains(modelId);

    /// <summary>Atomically replaces the entire node→models map with the given snapshot.</summary>
    public void ReplaceAll(IReadOnlyDictionary<string, IReadOnlySet<string>> nodeModels)
    {
        ArgumentNullException.ThrowIfNull(nodeModels);
        var newStore = new ConcurrentDictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var (nodeId, models) in nodeModels)
            newStore[nodeId] = models;
        Volatile.Write(ref _nodeModels, newStore);
    }
}
