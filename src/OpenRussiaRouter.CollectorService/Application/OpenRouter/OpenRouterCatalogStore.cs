namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed record OpenRouterCatalogSnapshot(
    IReadOnlyList<OpenRouterEntry> Entries,
    DateTimeOffset? CollectedAtUtc,
    bool Available)
{
    public static readonly OpenRouterCatalogSnapshot Empty = new([], null, false);
}

public sealed class OpenRouterCatalogStore
{
    private volatile OpenRouterCatalogSnapshot _current = OpenRouterCatalogSnapshot.Empty;
    public OpenRouterCatalogSnapshot Current => _current;
    public void Set(OpenRouterCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = snapshot;
    }
}
