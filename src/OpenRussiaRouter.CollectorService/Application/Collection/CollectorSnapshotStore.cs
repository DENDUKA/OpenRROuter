namespace OpenRussiaRouter.CollectorService.Application.Collection;

/// <summary>
/// Holds the most recently collected snapshot. Reads are lock-free; writes swap the
/// reference atomically so consumers always see a fully-built, consistent snapshot.
/// </summary>
public sealed class CollectorSnapshotStore
{
    private volatile CollectorSnapshot _current = CollectorSnapshot.Empty;

    public CollectorSnapshot Current => _current;

    public void Set(CollectorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = snapshot;
    }
}
