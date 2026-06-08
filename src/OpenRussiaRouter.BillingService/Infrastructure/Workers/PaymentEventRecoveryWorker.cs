using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BillingService.Application.Services;

namespace OpenRussiaRouter.BillingService.Infrastructure.Workers;

public sealed class PaymentEventRecoveryWorker(
    IBillingRepository repository,
    ILogger<PaymentEventRecoveryWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task ProcessBatchAsync(CancellationToken ct)
    {
        try
        {
            var processed = await repository.AcquireUnprocessedEventsAsync(
                BatchSize, DateTimeOffset.UtcNow, ct: ct).ConfigureAwait(false);
            if (processed == 0)
                return;

            logger.LogInformation("PaymentEventRecoveryWorker: recovered {Count} unprocessed event(s).", processed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "PaymentEventRecoveryWorker batch failed.");
        }
    }
}
