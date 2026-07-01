using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed class UsageOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<UsageOutboxOptions> options,
    ILogger<UsageOutboxDispatcher> logger) : BackgroundService
{
    private readonly UsageOutboxOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Usage outbox dispatcher failed unexpectedly.");
            }

            try
            {
                await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task DispatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUsageOutboxRepository>();
        var billingProvider = scope.ServiceProvider.GetRequiredService<IBillingProvider>();

        var now = DateTimeOffset.UtcNow;
        var records = await repository.ClaimPendingAsync(
            options.BatchSize,
            options.LeaseDuration,
            now,
            stoppingToken).ConfigureAwait(false);

        foreach (var record in records)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var ev = record.ToGatewayUsageEvent();
                var accepted = await billingProvider.RecordUsageAsync(ev, stoppingToken).ConfigureAwait(false);

                if (accepted)
                {
                    await repository.MarkSentAsync(record.RequestId, stoppingToken).ConfigureAwait(false);
                    logger.LogDebug(
                        "Usage outbox record sent. RequestId: {RequestId}",
                        record.RequestId);
                }
                else
                {
                    logger.LogWarning(
                        "Billing service rejected usage record. RequestId: {RequestId}",
                        record.RequestId);
                    await repository.MarkSentAsync(record.RequestId, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException ex)
            {
                await HandleFailureAsync(repository, record, ex, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(repository, record, ex, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleFailureAsync(
        IUsageOutboxRepository repository,
        UsageOutboxRecord record,
        Exception ex,
        CancellationToken stoppingToken)
    {
        var attempts = record.Attempts + 1;
        var nextAttempt = DateTimeOffset.UtcNow.Add(CalculateBackoff(attempts));

        if (attempts >= options.MaxAttempts)
        {
            logger.LogError(
                ex,
                "Usage outbox delivery exceeded max attempts. RequestId: {RequestId}; Attempts: {Attempts}",
                record.RequestId,
                attempts);
        }
        else
        {
            logger.LogWarning(
                ex,
                "Usage outbox delivery failed. RequestId: {RequestId}; Attempts: {Attempts}; NextAttempt: {NextAttempt}",
                record.RequestId,
                attempts,
                nextAttempt);
        }

        await repository.MarkFailedAsync(record.RequestId, attempts, nextAttempt, stoppingToken).ConfigureAwait(false);
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        var maxBackoff = TimeSpan.FromHours(1);

        // A row that keeps failing is retried past MaxAttempts (we never drop it), so `attempt`
        // grows unbounded. Compute in double and clamp before casting so the exponential can't
        // overflow Int64 into a negative TimeSpan (which would collapse the backoff to a hot loop).
        var scaledTicks = options.BaseBackoff.Ticks * Math.Pow(2, attempt - 1);
        return scaledTicks >= maxBackoff.Ticks
            ? maxBackoff
            : TimeSpan.FromTicks((long)scaledTicks);
    }
}
