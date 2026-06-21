using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace OpenRussiaRouter.CollectorService.Application.Collection;

public sealed class NodePollingWorker(
    INodeListProvider nodeListProvider,
    NodeCatalogCollector collector,
    CollectorSnapshotStore store,
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
