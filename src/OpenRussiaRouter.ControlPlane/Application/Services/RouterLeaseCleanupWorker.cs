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

    public void RunOnce(DateTimeOffset nowUtc)
    {
        if (!options.CurrentValue.Enabled)
        {
            return;
        }

        controlPlane.ReleaseExpiredLeases(nowUtc);
    }

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
                logger.LogWarning(exception, "Router lease cleanup round failed with error: {ErrorMessage}", exception.Message);
            }

            await Task.Delay(options.CurrentValue.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
