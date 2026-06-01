namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed class QuotaSnapshotStore
{
    private volatile QuotaSnapshot _current = QuotaSnapshot.Empty;
    public QuotaSnapshot Current => _current;
    public void Set(QuotaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = snapshot;
    }
}
