namespace OpenRussiaRouter.CollectorService.Application.Collection;

public sealed record NodeCatalogResult(
    string NodeId,
    bool Unreachable,
    IReadOnlyList<ModelObservation> Observations,
    string? Request,
    string? Response);

public sealed class ModelObservation
{
    public string UpstreamId { get; init; } = string.Empty;
    public string PublicId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ModelType { get; init; } = string.Empty;
    public IReadOnlyList<string> SourceProviders { get; init; } = [];
    public int Rank { get; init; }
    public string ConnectionId { get; init; } = string.Empty;
}
