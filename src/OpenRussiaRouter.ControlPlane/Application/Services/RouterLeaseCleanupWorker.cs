using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Periodically releases expired router leases so load counters recover from abandoned requests.
/// </summary>
public sealed class RouterLeaseCleanupWorker(
    RouterControlPlaneService controlPlane,
    IOptionsMonitor<RouterLeaseCleanupOptions> options,
    ILogger<RouterLeaseCleanupWorker> logger)
    : BackgroundService
{
    /// <summary>
    /// Runs one cleanup round.
    /// </summary>
    /// <param name="nowUtc">Current timestamp.</param>
    public void RunOnce(DateTimeOffset nowUtc)
    {
        if (!options.CurrentValue.Enabled)
        {
            return;
        }

        controlPlane.ReleaseExpiredLeases(nowUtc);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RunOnce(DateTimeOffset.UtcNow);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Router lease cleanup round failed.");
            }

            await Task.Delay(options.CurrentValue.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
