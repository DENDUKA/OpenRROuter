using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.CollectorService.Application.Billing;
using OpenRussiaRouter.CollectorService.Application.Quota;

namespace OpenRussiaRouter.CollectorService.Application.Collection;

public sealed class NodePollingWorker(
    INodeListProvider nodeListProvider,
    NodeCatalogCollector collector,
    NodeQuotaCollector quotaCollector,
    IBillingModelCatalogProvider billingModelCatalogProvider,
    CollectorSnapshotStore store,
    QuotaSnapshotStore quotaStore,
    IOptionsMonitor<CollectorOptions> options,
    ILogger<NodePollingWorker> logger,
    TimeProvider timeProvider)

    : BackgroundService
{
    private IReadOnlyList<CollectorNode>? _cachedNodes;
    private DateTimeOffset? _nodesFetchedAtUtc;

    /// <summary>One poll cycle: refresh the node list when stale, collect each routable node, swap snapshot.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["PollCycleId"] = Guid.NewGuid()
        });

        var now = timeProvider.GetUtcNow();
        var nodes = await GetNodeListAsync(now, cancellationToken).ConfigureAwait(false);
        if (nodes is null)
        {
            // No node list available yet (ControlPlane unreachable on the very first fetch);
            // keep the previous snapshot.
            return;
        }

        var routable = nodes.Where(NodeCatalogCollector.IsRoutableForCatalog).ToList();

        var catalogResults = await Task.WhenAll(
            routable.Select(node => collector.CollectAsync(node, cancellationToken))).ConfigureAwait(false);
        store.Set(new CollectorSnapshot(catalogResults, now));

        var quotaResults = await Task.WhenAll(
            routable.Select(node => quotaCollector.CollectAsync(node, cancellationToken))).ConfigureAwait(false);
        var catalogModels = await billingModelCatalogProvider.ListModels(cancellationToken).ConfigureAwait(false);
        var flatMeasurements = quotaResults.SelectMany(r => r.Measurements).ToList();
        var quotaStatuses = quotaResults.SelectMany(r => r.Statuses).ToList();

        var catalogsByNodeId = catalogResults.ToDictionary(c => c.NodeId, c => c, StringComparer.Ordinal);

        var enrichedMeasurements = new List<QuotaMeasurement>(flatMeasurements.Count);

        foreach (var measurement in flatMeasurements)
        {
            catalogsByNodeId.TryGetValue(measurement.NodeId, out var nodeCatalog);
            List<string> modelIds = [];

            if (measurement.ModelIds.Count > 0)
            {
                modelIds = measurement.ModelIds.Distinct(StringComparer.Ordinal).ToList();
            }
            else if (nodeCatalog != null)
            {
                modelIds = nodeCatalog.Observations
                    .Where(o => string.Equals(o.ConnectionId, measurement.ConnectionId, StringComparison.OrdinalIgnoreCase))
                    .Select(o => string.IsNullOrWhiteSpace(o.PublicId) ? o.UpstreamId : o.PublicId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }

            enrichedMeasurements.Add(measurement with { ModelIds = modelIds });
        }

        var activeModelMeasurements = BuildActiveModelMeasurements(catalogsByNodeId, flatMeasurements, catalogModels);
        var modelCatalog = BuildModelCatalog(catalogsByNodeId, catalogModels);
        quotaStore.Set(new QuotaSnapshot(
            enrichedMeasurements, now, activeModelMeasurements, quotaStatuses, modelCatalog));
    }

    /// <summary>
    /// Что каждая нода отдаёт на каждом подключении и как это соотносится с каталогом ORR.
    /// Модели, которых в каталоге нет, остаются в списке со статусом <see cref="ModelBillingStates.Absent"/> —
    /// именно ради них снят фильтр по Enabled в <see cref="IBillingModelCatalogProvider"/>.
    /// </summary>
    private static IReadOnlyList<NodeModelCatalogEntry> BuildModelCatalog(
        IReadOnlyDictionary<string, NodeCatalogResult> catalogsByNodeId,
        IReadOnlyList<BillingCatalogModel> catalogModels)
    {
        var byRouterModelId = GroupByRouterModelId(catalogModels);
        var entries = new List<NodeModelCatalogEntry>();

        foreach (var nodeCatalog in catalogsByNodeId.Values.Where(c => !c.Unreachable))
        {
            foreach (var observation in nodeCatalog.Observations)
            {
                if (string.IsNullOrWhiteSpace(observation.ConnectionId))
                    continue;

                // Квоты здесь не участвуют, поэтому второго источника имени провайдера нет.
                var provider = ResolveProvider(observation.Provider, quotaProvider: string.Empty);
                var matched = FindCatalogModels(byRouterModelId, observation);

                if (matched.Count == 0)
                {
                    var routableId = string.IsNullOrWhiteSpace(observation.PublicId)
                        ? observation.UpstreamId
                        : observation.PublicId;
                    if (string.IsNullOrWhiteSpace(routableId))
                        continue;

                    entries.Add(new NodeModelCatalogEntry(
                        nodeCatalog.NodeId,
                        provider,
                        observation.ConnectionId,
                        routableId,
                        routableId,
                        ModelBillingStates.Absent));
                    continue;
                }

                foreach (var model in matched)
                {
                    entries.Add(new NodeModelCatalogEntry(
                        nodeCatalog.NodeId,
                        provider,
                        observation.ConnectionId,
                        model.ModelId,
                        model.RouterModelId,
                        model.Enabled ? ModelBillingStates.Enabled : ModelBillingStates.Disabled));
                }
            }
        }

        // Одна и та же модель приходит несколькими наблюдениями (прямой маршрут + combo-алиас),
        // а метка Prometheus должна быть уникальной — иначе гейдж отдаст дубли серий.
        return entries
            .DistinctBy(entry => (entry.NodeId, entry.ConnectionId, entry.ModelId))
            .ToList();
    }

    /// <summary>
    /// Квоты в разрезе активных моделей каталога. Выключенные модели сюда попадать не должны —
    /// метрика orr_active_model_quota_percent означает «то, что мы продаём».
    /// </summary>
    private static IReadOnlyList<ActiveModelQuotaMeasurement> BuildActiveModelMeasurements(
        IReadOnlyDictionary<string, NodeCatalogResult> catalogsByNodeId,
        IReadOnlyList<QuotaMeasurement> flatMeasurements,
        IReadOnlyList<BillingCatalogModel> catalogModels)
    {
        var activeModels = catalogModels.Where(model => model.Enabled).ToList();
        if (activeModels.Count == 0 || flatMeasurements.Count == 0)
            return [];

        var activeByRouterModelId = GroupByRouterModelId(activeModels);

        var quotasByNodeAndConnection = flatMeasurements
            .Where(m => m.Percent.HasValue)
            .ToLookup(
                m => (NodeId: m.NodeId, ConnectionId: m.ConnectionId),
                m => m,
                new NodeConnectionComparer());

        var measurements = new List<ActiveModelQuotaMeasurement>();
        foreach (var nodeCatalog in catalogsByNodeId.Values.Where(c => !c.Unreachable))
        {
            foreach (var observation in nodeCatalog.Observations)
            {
                if (string.IsNullOrWhiteSpace(observation.ConnectionId))
                    continue;

                var matchedModels = FindCatalogModels(activeByRouterModelId, observation);
                if (matchedModels.Count == 0)
                    continue;

                var connectionQuotas = quotasByNodeAndConnection[new(nodeCatalog.NodeId, observation.ConnectionId)].ToList();
                if (connectionQuotas.Count == 0)
                    continue;

                foreach (var activeModel in matchedModels)
                {
                    var quota = FindModelSpecificQuota(connectionQuotas, activeModel, observation)
                        ?? connectionQuotas.FirstOrDefault(measurement => measurement.ModelIds.Count == 0);
                    var percent = quota?.Percent ?? connectionQuotas.Average(measurement => measurement.Percent!.Value);
                    var provider = ResolveProvider(observation.Provider, quota?.Provider ?? connectionQuotas[0].Provider);

                    measurements.Add(new ActiveModelQuotaMeasurement(
                        nodeCatalog.NodeId,
                        provider,
                        observation.ConnectionId,
                        activeModel.ModelId,
                        activeModel.RouterModelId,
                        percent));
                }
            }
        }

        return measurements
            .GroupBy(measurement => new
            {
                measurement.NodeId,
                measurement.Provider,
                measurement.ConnectionId,
                measurement.ModelId,
                measurement.RouterModelId
            })
            .Select(group => new ActiveModelQuotaMeasurement(
                group.Key.NodeId,
                group.Key.Provider,
                group.Key.ConnectionId,
                group.Key.ModelId,
                group.Key.RouterModelId,
                group.Average(measurement => measurement.Percent)))
            .ToList();
    }

    private static Dictionary<string, List<BillingCatalogModel>> GroupByRouterModelId(
        IReadOnlyList<BillingCatalogModel> models) =>
        models
            .GroupBy(model => model.RouterModelId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

    /// <summary>Модели каталога, соответствующие наблюдению ноды: матч по RouterModelId ↔ UpstreamId/PublicId.</summary>
    private static IReadOnlyList<BillingCatalogModel> FindCatalogModels(
        IReadOnlyDictionary<string, List<BillingCatalogModel>> modelsByRouterModelId,
        ModelObservation observation)
    {
        var result = new List<BillingCatalogModel>();
        if (!string.IsNullOrWhiteSpace(observation.UpstreamId) &&
            modelsByRouterModelId.TryGetValue(observation.UpstreamId, out var upstreamMatches))
        {
            result.AddRange(upstreamMatches);
        }

        if (!string.IsNullOrWhiteSpace(observation.PublicId) &&
            modelsByRouterModelId.TryGetValue(observation.PublicId, out var publicMatches))
        {
            result.AddRange(publicMatches);
        }

        return result
            .DistinctBy(model => model.ModelId, StringComparer.Ordinal)
            .ToList();
    }

    private static QuotaMeasurement? FindModelSpecificQuota(
        IReadOnlyList<QuotaMeasurement> connectionQuotas,
        BillingCatalogModel activeModel,
        ModelObservation observation)
    {
        var candidateIds = new[]
        {
            activeModel.RouterModelId,
            activeModel.ModelId,
            observation.UpstreamId,
            observation.PublicId
        };

        return connectionQuotas.FirstOrDefault(measurement =>
            measurement.ModelIds.Count > 0 &&
            measurement.ModelIds.Any(modelId =>
                candidateIds.Any(candidate =>
                    string.Equals(modelId, candidate, StringComparison.Ordinal))));
    }

    private static string ResolveProvider(string observationProvider, string quotaProvider)
    {
        if (!string.IsNullOrWhiteSpace(observationProvider))
            return observationProvider;
        if (!string.IsNullOrWhiteSpace(quotaProvider))
            return quotaProvider;
        return "unknown";
    }

    private sealed class NodeConnectionComparer : IEqualityComparer<(string NodeId, string ConnectionId)>
    {
        public bool Equals((string NodeId, string ConnectionId) x, (string NodeId, string ConnectionId) y) =>
            string.Equals(x.NodeId, y.NodeId, StringComparison.Ordinal) &&
            string.Equals(x.ConnectionId, y.ConnectionId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string NodeId, string ConnectionId) obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.NodeId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ConnectionId));
    }

    /// <summary>

    /// Returns the node list, refreshing from ControlPlane only when the cache is empty or older
    /// than <see cref="CollectorOptions.NodeListRefreshInterval"/>. On a refresh failure a previously
    /// cached list is reused (stale); a null result means no list has ever been fetched.
    /// </summary>
    private async Task<IReadOnlyList<CollectorNode>?> GetNodeListAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var refreshInterval = options.CurrentValue.NodeListRefreshInterval;
        var isStale = _cachedNodes is null
            || _nodesFetchedAtUtc is null
            || now - _nodesFetchedAtUtc.Value >= refreshInterval;

        if (!isStale)
            return _cachedNodes;

        try
        {
            var nodes = await nodeListProvider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
            _cachedNodes = nodes;
            _nodesFetchedAtUtc = now;
            return nodes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_cachedNodes is not null)
            {
                logger.LogWarning(ex,
                    "Failed to refresh node list from ControlPlane; reusing cached list of {Count} node(s)",
                    _cachedNodes.Count);
                return _cachedNodes;
            }

            logger.LogWarning(ex, "Failed to fetch node list from ControlPlane; keeping previous snapshot");
            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Node polling cycle failed");
            }

            try
            {
                await Task.Delay(options.CurrentValue.NodePollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
