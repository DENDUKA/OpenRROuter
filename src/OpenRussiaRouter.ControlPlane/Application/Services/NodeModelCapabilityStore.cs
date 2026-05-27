using System.Collections.Concurrent;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Thread-safe in-memory store that maps each router node to its set of supported model IDs.
/// Populated by <see cref="RouterModelCapabilityJob"/> on a periodic schedule.
/// </summary>
public sealed class NodeModelCapabilityStore
{
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _nodeModels = new(StringComparer.Ordinal);

    /// <summary>Returns true if no node model data has been collected yet (e.g. right after startup).</summary>
    public bool IsEmpty => _nodeModels.IsEmpty;

    /// <summary>Updates the model list for the given node.</summary>
    public void SetNodeModels(string nodeId, IReadOnlySet<string> modelIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(modelIds);
        _nodeModels[nodeId] = modelIds;
    }

    /// <summary>Removes a node from the store (e.g. when a node is deleted or disabled).</summary>
    public void ClearNode(string nodeId) => _nodeModels.TryRemove(nodeId, out _);

    /// <summary>Returns true if the node has the requested model in its capability list.</summary>
    public bool NodeSupportsModel(string nodeId, string modelId)
        => _nodeModels.TryGetValue(nodeId, out var models)
           && models.Contains(modelId);
}
