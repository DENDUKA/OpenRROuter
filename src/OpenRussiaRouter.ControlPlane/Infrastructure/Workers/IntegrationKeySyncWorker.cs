using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Workers;

/// <summary>
/// Keeps every enabled node's key table converged on AuthService.
///
/// Why this exists next to the per-creation push: that push is best-effort (WebApp logs a
/// failure and still hands the key to the user), so any node that was down at that moment,
/// or that was registered later with an empty key table, stayed permanently out of sync and
/// answered 401 to the whole fleet. A node is not "configured" until its keys actually
/// arrived, and nothing else retries.
///
/// It runs a full sweep rather than chasing individual keys: the node's bulk endpoint is
/// idempotent and diff-based, so a sweep costs one HTTP call per node and repairs every
/// kind of drift at once — missing keys, stale active flags, keys revoked while a node was
/// offline.
/// </summary>
public sealed class IntegrationKeySyncWorker(
    IntegrationKeySyncService syncService,
    IOptionsMonitor<IntegrationKeySyncOptions> options,
    ILogger<IntegrationKeySyncWorker> logger)
    : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!options.CurrentValue.Enabled)
            return;

        var nodes = await syncService.ListEnabledNodesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var node in nodes)
        {
            try
            {
                var result = await syncService.SyncNodeAsync(node, cancellationToken).ConfigureAwait(false);
                if (result is null)
                {
                    // Already logged with the node's own error by IntegrationKeySyncService.
                    continue;
                }

                // Log only when something actually moved; a fleet in steady state would
                // otherwise write one line per node every interval for no reason.
                var changed = result.AddedCount + result.DisabledCount + result.ReactivatedCount + result.DeletedCount > 0;
                if (changed)
                {
                    logger.LogInformation(
                        "Integration keys reconciled on node {NodeId} ({NodeName}): added={Added}, disabled={Disabled}, reactivated={Reactivated}, deleted={Deleted}",
                        node.Id, node.Name, result.AddedCount, result.DisabledCount, result.ReactivatedCount, result.DeletedCount);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Integration key sync round failed for node {NodeId}: {ErrorMessage}", node.Id, ex.Message);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.CurrentValue;

        // Let AuthService finish starting before the first sweep.
        if (configured.InitialDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(configured.InitialDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Integration key sync sweep failed: {ErrorMessage}", exception.Message);
            }

            await Task.Delay(options.CurrentValue.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
