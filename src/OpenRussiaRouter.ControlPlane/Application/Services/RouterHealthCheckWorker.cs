using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Interfaces;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Periodically refreshes health status for enabled external 9Router nodes.
/// </summary>
public sealed class RouterHealthCheckWorker(
    RouterControlPlaneService controlPlane,
    IRouterNodeHealthClient healthClient,
    IOptionsMonitor<RouterHealthCheckOptions> options,
    ILogger<RouterHealthCheckWorker> logger)
    : BackgroundService
{
    /// <summary>
    /// Runs one health check round.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            return;
        }

        var nodes = controlPlane.ListNodesForHealthChecks();
        foreach (var node in nodes.Where(candidate => candidate.Enabled))
        {
            var result = await healthClient.CheckAsync(node, cancellationToken).ConfigureAwait(false);
            controlPlane.ApplyHealth(
                node.Id,
                result.Status,
                DateTimeOffset.UtcNow,
                result.LatencyMs,
                result.ErrorCode);
        }
    }

    /// <inheritdoc />
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
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Router health check round failed.");
            }

            await Task.Delay(options.CurrentValue.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
