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
    /// <summary>One poll cycle: fetch node list, collect each routable node, swap snapshot.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CollectorNode> nodes;
        try
        {
            nodes = await nodeListProvider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the previous snapshot if ControlPlane is unreachable.
            logger.LogWarning(ex, "Failed to fetch node list from ControlPlane; keeping previous snapshot");
            return;
        }

        var routable = nodes.Where(NodeCatalogCollector.IsRoutableForCatalog).ToList();
        var results = await Task.WhenAll(
            routable.Select(node => collector.CollectAsync(node, cancellationToken))).ConfigureAwait(false);

        store.Set(new CollectorSnapshot(results, timeProvider.GetUtcNow()));
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
