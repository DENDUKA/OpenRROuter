using OpenRussiaRouter.Contracts.Grpc.Collector.V1;
using OpenRussiaRouter.WebApp.Services.ControlPlane; // NodeModelCatalogResult, NodeModelCatalogEntry, UnreachableNodeDetail
using CollectorGrpc = OpenRussiaRouter.Contracts.Grpc.Collector.V1.Collector;

namespace OpenRussiaRouter.WebApp.Services.Collector;

public sealed class CollectorProvider(
    CollectorGrpc.CollectorClient client,
    ILogger<CollectorProvider> logger)
{
    private static readonly TimeSpan NodeModelCatalogTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan NodeModelDropdownTimeout = TimeSpan.FromSeconds(8);

    public async Task<NodeModelCatalogResult> ListNodeModelCatalogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(NodeModelCatalogTimeout);
            var response = await client.ListNodeModelCatalogAsync(
                new ListNodeModelCatalogRequest(), cancellationToken: timeoutCts.Token);

            return new NodeModelCatalogResult(
                Entries: response.Entries.Select(e => new NodeModelCatalogEntry(
                    UpstreamId: e.UpstreamId,
                    PublicId: e.PublicId,
                    DisplayName: e.DisplayName,
                    ModelType: e.ModelType,
                    SourceProviders: e.SourceProviders.ToList(),
                    NodeIds: e.NodeIds.ToList(),
                    SupportsReasoning: e.SupportsReasoning,
                    ThinkingFormat: string.IsNullOrWhiteSpace(e.ThinkingFormat) ? null : e.ThinkingFormat,
                    ThinkingCanDisable: e.ThinkingCanDisable,
                    ThinkingOptions: e.ThinkingOptions.ToList())).ToList(),
                UnreachableNodeIds: response.UnreachableNodeIds.ToList(),
                UnreachableNodes: response.UnreachableNodes.Select(u => new UnreachableNodeDetail(
                    NodeId: u.NodeId, Request: u.Request, Response: u.Response)).ToList(),
                Error: null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load node model catalog from collector");
            return new NodeModelCatalogResult([], [], [], $"Failed to load node model catalog: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> GetNodeModelsAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(NodeModelDropdownTimeout);
            var response = await client.GetNodeModelsAsync(
                new GetNodeModelsRequest { NodeId = nodeId ?? string.Empty }, cancellationToken: timeoutCts.Token);
            return string.IsNullOrWhiteSpace(response.Error) ? response.PublicIds.ToList() : [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get models for node {NodeId} from collector", nodeId);
            return [];
        }
    }

    /// <summary>Distinct routable model ids across all nodes — derived from the catalog entries.</summary>
    public async Task<IReadOnlyList<string>> ListAvailableNodeModelsAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await ListNodeModelCatalogAsync(cancellationToken);
        return catalog.Entries
            .Select(e => e.PublicId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }
}
