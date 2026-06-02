using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Periodically pulls the model-capability snapshot from the CollectorService and updates the
/// local <see cref="NodeModelCapabilityStore"/> used by the routing hot path. Replaces the
/// in-process Quartz polling job; routing latency is unchanged because reads stay local.
/// </summary>
public sealed class CapabilityReplicaRefresher(
    Collector.CollectorClient collectorClient,
    NodeModelCapabilityStore store,
    IOptionsMonitor<CapabilityReplicaOptions> options,
    ILogger<CapabilityReplicaRefresher> logger)
    : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
            return;

        GetCapabilitiesSnapshotResponse response;
        try
        {
            response = await collectorClient
                .GetCapabilitiesSnapshotAsync(new GetCapabilitiesSnapshotRequest(), cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the existing store on a transient collector outage — routing keeps using stale hints.
            logger.LogWarning(ex, "Capability snapshot refresh from collector failed; keeping existing replica");
            return;
        }

        var map = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var node in response.Nodes)
            map[node.NodeId] = node.ModelIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        store.ReplaceAll(map);
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
                logger.LogError(ex, "Capability replica refresh cycle failed");
            }

            try
            {
                await Task.Delay(options.CurrentValue.RefreshInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
