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
    IActiveBillingModelProvider activeBillingModelProvider,
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
        var activeModels = await activeBillingModelProvider.ListActiveModelsAsync(cancellationToken).ConfigureAwait(false);
        var flatMeasurements = quotaResults.SelectMany(m => m).ToList();
        var enrichedMeasurements = new List<QuotaMeasurement>(flatMeasurements.Count);

        foreach (var measurement in flatMeasurements)
        {
            var nodeCatalog = catalogResults.FirstOrDefault(c => c.NodeId == measurement.NodeId);
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

        var activeModelMeasurements = BuildActiveModelMeasurements(catalogResults, flatMeasurements, activeModels);
        quotaStore.Set(new QuotaSnapshot(enrichedMeasurements, now, activeModelMeasurements));
    }

    private static IReadOnlyList<ActiveModelQuotaMeasurement> BuildActiveModelMeasurements(
        IReadOnlyList<NodeCatalogResult> catalogResults,
        IReadOnlyList<QuotaMeasurement> flatMeasurements,
        IReadOnlyList<ActiveBillingModel> activeModels)
    {
        if (activeModels.Count == 0 || flatMeasurements.Count == 0)
            return [];

        var activeByRouterModelId = activeModels
            .GroupBy(model => model.RouterModelId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var measurements = new List<ActiveModelQuotaMeasurement>();
        foreach (var nodeCatalog in catalogResults.Where(c => !c.Unreachable))
        {
            foreach (var observation in nodeCatalog.Observations)
            {
                if (string.IsNullOrWhiteSpace(observation.ConnectionId))
                    continue;

                var matchedModels = FindActiveModels(activeByRouterModelId, observation);
                if (matchedModels.Count == 0)
                    continue;

                var connectionQuotas = flatMeasurements
                    .Where(measurement =>
                        string.Equals(measurement.NodeId, nodeCatalog.NodeId, StringComparison.Ordinal) &&
                        string.Equals(measurement.ConnectionId, observation.ConnectionId, StringComparison.OrdinalIgnoreCase) &&
                        measurement.Percent.HasValue)
                    .ToList();
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

    private static IReadOnlyList<ActiveBillingModel> FindActiveModels(
        IReadOnlyDictionary<string, List<ActiveBillingModel>> activeByRouterModelId,
        ModelObservation observation)
    {
        var result = new List<ActiveBillingModel>();
        if (!string.IsNullOrWhiteSpace(observation.UpstreamId) &&
            activeByRouterModelId.TryGetValue(observation.UpstreamId, out var upstreamMatches))
        {
            result.AddRange(upstreamMatches);
        }

        if (!string.IsNullOrWhiteSpace(observation.PublicId) &&
            activeByRouterModelId.TryGetValue(observation.PublicId, out var publicMatches))
        {
            result.AddRange(publicMatches);
        }

        return result
            .DistinctBy(model => model.ModelId, StringComparer.Ordinal)
            .ToList();
    }

    private static QuotaMeasurement? FindModelSpecificQuota(
        IReadOnlyList<QuotaMeasurement> connectionQuotas,
        ActiveBillingModel activeModel,
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
