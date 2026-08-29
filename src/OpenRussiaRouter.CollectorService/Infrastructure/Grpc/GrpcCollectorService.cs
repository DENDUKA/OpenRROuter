using Grpc.Core;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.OpenRouter;
using OpenRussiaRouter.CollectorService.Application.Quota;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.CollectorService.Infrastructure.Grpc;

public sealed class GrpcCollectorService(
    CollectorSnapshotStore store,
    QuotaSnapshotStore quotaStore,
    OpenRouterCatalogStore openRouterStore)
    : Collector.CollectorBase
{
    public override Task<GetCapabilitiesSnapshotResponse> GetCapabilitiesSnapshot(
        GetCapabilitiesSnapshotRequest request, ServerCallContext context)
    {
        var snapshot = store.Current;
        var response = new GetCapabilitiesSnapshotResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty
        };
        foreach (var (nodeId, modelIds) in CatalogProjection.Capabilities(snapshot))
        {
            var cap = new NodeCapability { NodeId = nodeId };
            cap.ModelIds.AddRange(modelIds);
            response.Nodes.Add(cap);
        }
        return Task.FromResult(response);
    }

    public override Task<ListNodeModelCatalogResponse> ListNodeModelCatalog(
        ListNodeModelCatalogRequest request, ServerCallContext context)
    {
        var snapshot = store.Current;
        var response = new ListNodeModelCatalogResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty
        };

        foreach (var entry in CatalogProjection.Catalog(snapshot))
        {
            var e = new UpstreamModelEntry
            {
                UpstreamId = entry.UpstreamId,
                PublicId = entry.PublicId,
                DisplayName = entry.DisplayName,
                ModelType = entry.ModelType,
                SupportsReasoning = entry.SupportsReasoning,
                ThinkingFormat = entry.ThinkingFormat ?? string.Empty,
                ThinkingCanDisable = entry.ThinkingCanDisable
            };
            e.SourceProviders.AddRange(entry.SourceProviders);
            e.NodeIds.AddRange(entry.NodeIds);
            e.ThinkingOptions.AddRange(entry.ThinkingOptions);
            e.ReasoningEfforts.AddRange(entry.ReasoningEfforts);
            foreach (var member in entry.ComboReasoningMembers)
            {
                var comboMember = new OpenRussiaRouter.Contracts.Grpc.Collector.V1.ComboReasoningMember
                {
                    ModelId = member.ModelId
                };
                comboMember.ReasoningEfforts.AddRange(member.ReasoningEfforts);
                e.ComboReasoningMembers.Add(comboMember);
            }
            response.Entries.Add(e);
        }

        foreach (var node in snapshot.Nodes.Where(n => n.Unreachable).OrderBy(n => n.NodeId, StringComparer.Ordinal))
        {
            response.UnreachableNodeIds.Add(node.NodeId);
            response.UnreachableNodes.Add(new UnreachableNodeInfo
            {
                NodeId = node.NodeId,
                Request = node.Request ?? string.Empty,
                Response = node.Response ?? string.Empty
            });
        }

        return Task.FromResult(response);
    }

    public override Task<GetNodeModelsResponse> GetNodeModels(
        GetNodeModelsRequest request, ServerCallContext context)
    {
        var snapshot = store.Current;
        var models = CatalogProjection.NodeModels(snapshot, request.NodeId);
        if (models is null)
            return Task.FromResult(new GetNodeModelsResponse { Error = "Node not found or unreachable" });

        var response = new GetNodeModelsResponse();
        response.PublicIds.AddRange(models);
        return Task.FromResult(response);
    }

    public override Task<GetProviderQuotaResponse> GetProviderQuota(
        GetProviderQuotaRequest request, ServerCallContext context)
    {
        var snapshot = quotaStore.Current;
        var response = new GetProviderQuotaResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty
        };
        foreach (var m in snapshot.Measurements)
        {
            response.Measurements.Add(new ProviderQuotaMeasurement
            {
                NodeId = m.NodeId,
                Provider = m.Provider,
                ConnectionId = m.ConnectionId,
                Remaining = m.Remaining
            });
        }
        return Task.FromResult(response);
    }

    public override Task<GetOpenRouterCatalogResponse> GetOpenRouterCatalog(
        GetOpenRouterCatalogRequest request, ServerCallContext context)
    {
        var snapshot = openRouterStore.Current;
        var response = new GetOpenRouterCatalogResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty,
            Available = snapshot.Available
        };
        foreach (var e in snapshot.Entries)
        {
            var entry = new OpenRouterCatalogEntry
            {
                Slug = e.Slug,
                Name = e.Name,
                AuthorDisplayName = e.AuthorDisplayName,
                Description = e.Description,
                ContextLength = e.ContextLength,
                PricePrompt = e.PricePrompt,
                PriceCompletion = e.PriceCompletion,
                PriceInputCacheRead = e.PriceInputCacheRead
            };
            entry.InputModalities.AddRange(e.InputModalities);
            response.Entries.Add(entry);
        }
        return Task.FromResult(response);
    }
}
