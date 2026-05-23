namespace OpenRussiaRouter.WebApp.Services.ControlPlane;

public sealed record NodeModelCatalogResult(
    IReadOnlyList<NodeModelCatalogEntry> Entries,
    IReadOnlyList<string> UnreachableNodeIds,
    string? Error);

public sealed record NodeModelCatalogEntry(
    string UpstreamId,
    string PublicId,
    string DisplayName,
    string ModelType,
    IReadOnlyList<string> SourceProviders,
    IReadOnlyList<string> NodeIds);