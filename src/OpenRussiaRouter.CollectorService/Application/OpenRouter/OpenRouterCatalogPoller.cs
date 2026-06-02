using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed class OpenRouterCatalogPoller(
    OpenRouterCatalogClient client,
    OpenRouterCatalogStore store,
    IOptionsMonitor<OpenRouterCatalogOptions> options,
    ILogger<OpenRouterCatalogPoller> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
            return;

        var entries = await client.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            // Keep the previous snapshot on a failed/empty fetch.
            logger.LogWarning("OpenRouter catalog fetch returned no entries; keeping previous snapshot");
            return;
        }
        store.Set(new OpenRouterCatalogSnapshot(entries, timeProvider.GetUtcNow(), Available: true));
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
                logger.LogError(ex, "OpenRouter catalog poll failed");
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
