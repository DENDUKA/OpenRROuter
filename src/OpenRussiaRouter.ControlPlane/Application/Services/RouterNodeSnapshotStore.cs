using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Holds the last complete, immutable routing-metadata snapshot. Dynamic load is deliberately
/// excluded: the repository remains the sole authority for least-load selection.
/// </summary>
public sealed class RouterNodeSnapshotStore
{
    private RouterNodeRoutingMetadata[] _nodes = [];

    public IReadOnlyList<RouterNodeRoutingMetadata> GetSnapshot() => Volatile.Read(ref _nodes);

    public void ReplaceAll(IEnumerable<RouterNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var snapshot = nodes.Select(node => new RouterNodeRoutingMetadata(
                node.Id,
                node.Name,
                node.EndpointUrl,
                node.ControlToken,
                node.CapacityWeight,
                node.Enabled,
                node.Status))
            .ToArray();
        Volatile.Write(ref _nodes, snapshot);
    }

    public void Upsert(RouterNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var item = ToMetadata(node);
        var current = Volatile.Read(ref _nodes);
        var existingIndex = Array.FindIndex(current, candidate => string.Equals(candidate.Id, item.Id, StringComparison.Ordinal));
        var next = existingIndex < 0
            ? [.. current, item]
            : current.Select((candidate, index) => index == existingIndex ? item : candidate).ToArray();
        Volatile.Write(ref _nodes, next);
    }

    public void Remove(string nodeId)
    {
        var current = Volatile.Read(ref _nodes);
        var next = current.Where(candidate => !string.Equals(candidate.Id, nodeId, StringComparison.Ordinal)).ToArray();
        Volatile.Write(ref _nodes, next);
    }

    private static RouterNodeRoutingMetadata ToMetadata(RouterNode node) => new(
        node.Id,
        node.Name,
        node.EndpointUrl,
        node.ControlToken,
        node.CapacityWeight,
        node.Enabled,
        node.Status);
}

public sealed record RouterNodeRoutingMetadata(
    string Id,
    string Name,
    Uri EndpointUrl,
    string ControlToken,
    int CapacityWeight,
    bool Enabled,
    RouterNodeStatus Status);
