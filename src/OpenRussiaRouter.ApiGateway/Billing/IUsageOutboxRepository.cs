namespace OpenRussiaRouter.ApiGateway.Billing;

public interface IUsageOutboxRepository
{
    Task EnqueueAsync(GatewayUsageEvent ev, CancellationToken ct);

    Task<IReadOnlyList<UsageOutboxRecord>> ClaimPendingAsync(
        int batchSize,
        TimeSpan lease,
        DateTimeOffset nowUtc,
        CancellationToken ct);

    Task MarkSentAsync(string requestId, CancellationToken ct);

    Task MarkFailedAsync(
        string requestId,
        int attempts,
        DateTimeOffset nextAttemptUtc,
        CancellationToken ct);
}
