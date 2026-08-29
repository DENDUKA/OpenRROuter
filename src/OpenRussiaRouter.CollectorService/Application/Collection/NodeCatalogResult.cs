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
    public string Provider { get; init; } = string.Empty;
    public bool SupportsReasoning { get; init; }
    public string? ThinkingFormat { get; init; }
    public bool ThinkingCanDisable { get; init; } = true;
    public IReadOnlyList<string> ThinkingOptions { get; init; } = [];
    public IReadOnlyList<string> ReasoningEfforts { get; init; } = [];
    public IReadOnlyList<ComboReasoningMember> ComboReasoningMembers { get; init; } = [];
}

public sealed record ComboReasoningMember(
    string ModelId,
    IReadOnlyList<string> ReasoningEfforts);
