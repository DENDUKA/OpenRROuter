namespace OpenRussiaRouter.CollectorService.Application.Collection;

public sealed record CollectorSnapshot(
    IReadOnlyList<NodeCatalogResult> Nodes,
    DateTimeOffset? CollectedAtUtc)
{
    public static readonly CollectorSnapshot Empty = new([], null);
}
