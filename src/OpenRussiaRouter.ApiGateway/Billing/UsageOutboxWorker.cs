using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ApiGateway.Application.Extensions;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed class UsageOutboxWorker(
    IUsageOutboxRepository repository,
    IServiceScopeFactory serviceScopeFactory,
    IOptionsMonitor<UsageOutboxOptions> options,
    ILogger<UsageOutboxWorker> logger)
    : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    public async Task RunOnceAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var billingClient = scope.ServiceProvider.GetRequiredService<IBillingGatewayClient>();
        var items = await repository.LeaseDueAsync(
            Math.Max(1, currentOptions.BatchSize),
            workerId,
            TimeSpan.FromSeconds(Math.Max(1, currentOptions.LeaseSeconds)),
            nowUtc,
            cancellationToken).ConfigureAwait(false);

        foreach (var item in items)
        {
            await DeliverAsync(item, billingClient, currentOptions, nowUtc, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Usage outbox worker round failed with error: {ErrorMessage}", exception.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.PollIntervalSeconds)), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task DeliverAsync(
        UsageOutboxItem item,
        IBillingGatewayClient billingClient,
        UsageOutboxOptions currentOptions,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var accepted = await billingClient.RecordUsageAsync(item.UsageEvent, cancellationToken).ConfigureAwait(false);
            if (accepted)
            {
                await repository.MarkDeliveredAsync(item.UsageEvent.RequestId, nowUtc, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Delivered usage outbox event. RequestId: {RequestId}; Attempts: {Attempts}", item.UsageEvent.RequestId, item.Attempts);
                return;
            }

            await MarkFailedAsync(item, currentOptions, nowUtc, "BillingService rejected usage event.", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await MarkFailedAsync(item, currentOptions, nowUtc, exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task MarkFailedAsync(
        UsageOutboxItem item,
        UsageOutboxOptions currentOptions,
        DateTimeOffset nowUtc,
        string lastError,
        CancellationToken cancellationToken)
    {
        var attempts = item.Attempts + 1;
        var dead = attempts >= Math.Max(1, currentOptions.MaxAttempts);
        var nextAttemptAtUtc = dead ? nowUtc : nowUtc.Add(ComputeBackoff(currentOptions, attempts));

        await repository.MarkFailedAsync(
            item.UsageEvent.RequestId,
            attempts,
            nextAttemptAtUtc,
            lastError,
            dead,
            nowUtc,
            cancellationToken).ConfigureAwait(false);

        if (dead)
        {
            logger.LogError(
                "Usage outbox event moved to dead status. RequestId: {RequestId}; Attempts: {Attempts}; Error: {Error}",
                item.UsageEvent.RequestId,
                attempts,
                lastError);
            return;
        }

        logger.LogWarning(
            "Usage outbox delivery failed. RequestId: {RequestId}; Attempts: {Attempts}; NextAttemptAtUtc: {NextAttemptAtUtc}; Error: {Error}",
            item.UsageEvent.RequestId,
            attempts,
            nextAttemptAtUtc,
            lastError);
    }

    private static TimeSpan ComputeBackoff(UsageOutboxOptions currentOptions, int attempts)
    {
        var baseSeconds = Math.Max(1, currentOptions.BaseBackoffSeconds);
        var maxSeconds = Math.Max(baseSeconds, currentOptions.MaxBackoffSeconds);
        var exponential = Math.Pow(2, Math.Min(attempts, 30)) * baseSeconds;
        var capped = Math.Min(exponential, maxSeconds);
        var jitter = 0.8d + (Random.Shared.NextDouble() * 0.4d);

        return TimeSpan.FromSeconds(Math.Min(capped * jitter, maxSeconds));
    }
}
