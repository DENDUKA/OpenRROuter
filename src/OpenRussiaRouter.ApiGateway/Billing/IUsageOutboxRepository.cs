namespace OpenRussiaRouter.ApiGateway.Billing;

public interface IUsageOutboxRepository
{
    Task EnqueueAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UsageOutboxItem>> LeaseDueAsync(
        int batchSize,
        string workerId,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task MarkDeliveredAsync(
        string requestId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        string requestId,
        int attempts,
        DateTimeOffset nextAttemptAtUtc,
        string lastError,
        bool dead,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public sealed record UsageOutboxItem(
    GatewayUsageEvent UsageEvent,
    int Attempts,
    DateTimeOffset NextAttemptAtUtc);
