namespace OpenRussiaRouter.WebApp.Services.ControlPlane;

public sealed record NodeModelCatalogResult(
    IReadOnlyList<NodeModelCatalogEntry> Entries,
    IReadOnlyList<string> UnreachableNodeIds,
    IReadOnlyList<UnreachableNodeDetail> UnreachableNodes,
    string? Error);

public sealed record NodeModelCatalogEntry(
    string UpstreamId,
    string PublicId,
    string DisplayName,
    string ModelType,
    IReadOnlyList<string> SourceProviders,
    IReadOnlyList<string> NodeIds,
    bool SupportsReasoning,
    string? ThinkingFormat,
    bool ThinkingCanDisable,
    IReadOnlyList<string> ThinkingOptions);

public sealed record UnreachableNodeDetail(
    string NodeId,
    string Request,
    string Response);