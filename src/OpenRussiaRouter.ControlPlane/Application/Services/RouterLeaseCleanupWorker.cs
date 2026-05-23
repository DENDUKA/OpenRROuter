using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class RouterLeaseCleanupWorker(
    RouterControlPlaneService controlPlane,
    IOptionsMonitor<RouterLeaseCleanupOptions> options,
    ILogger<RouterLeaseCleanupWorker> logger)
    : BackgroundService
{

    public async Task RunOnceAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (!options.CurrentValue.Enabled)
            return;

        await controlPlane.ReleaseExpiredLeasesAsync(nowUtc, ct).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Router lease cleanup round failed with error: {ErrorMessage}", exception.Message);
            }

            await Task.Delay(options.CurrentValue.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
