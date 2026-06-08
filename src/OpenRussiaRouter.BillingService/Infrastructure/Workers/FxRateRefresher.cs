using Microsoft.Extensions.Hosting;
using OpenRussiaRouter.BillingService.Application.Services;

namespace OpenRussiaRouter.BillingService.Infrastructure.Workers;

public sealed class FxRateRefresher(FxQuoteService fxQuoteService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await fxQuoteService.RefreshAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await fxQuoteService.RefreshAsync(stoppingToken).ConfigureAwait(false);
    }
}
