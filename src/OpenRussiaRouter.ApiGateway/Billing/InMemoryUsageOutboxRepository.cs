using Microsoft.Extensions.Logging;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed class InMemoryUsageOutboxRepository : IUsageOutboxRepository
{
    private readonly object syncRoot = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    public InMemoryUsageOutboxRepository(ILogger<InMemoryUsageOutboxRepository> logger)
    {
        logger.LogWarning("Gateway usage outbox is using non-durable in-memory storage. Configure ConnectionStrings:GatewayDb for production.");
    }

    public Task EnqueueAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usageEvent);
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            entries.TryAdd(
                usageEvent.RequestId,
                new Entry(usageEvent, "pending", 0, DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<UsageOutboxItem>> LeaseDueAsync(
        int batchSize,
        string workerId,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            var leased = entries.Values
                .Where(entry =>
                    (entry.Status == "pending" && entry.NextAttemptAtUtc <= nowUtc) ||
                    (entry.Status == "leased" && entry.LockedUntilUtc <= nowUtc))
                .OrderBy(entry => entry.NextAttemptAtUtc)
                .Take(batchSize)
                .ToArray();

            foreach (var entry in leased)
            {
                entry.Status = "leased";
                entry.LockedBy = workerId;
                entry.LockedUntilUtc = nowUtc.Add(leaseDuration);
                entry.UpdatedAtUtc = nowUtc;
            }

            return Task.FromResult<IReadOnlyCollection<UsageOutboxItem>>(
                leased.Select(entry => new UsageOutboxItem(entry.UsageEvent, entry.Attempts, entry.NextAttemptAtUtc)).ToArray());
        }
    }

    public Task MarkDeliveredAsync(string requestId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (entries.TryGetValue(requestId, out var entry))
            {
                entry.Status = "delivered";
                entry.LockedBy = null;
                entry.LockedUntilUtc = null;
                entry.UpdatedAtUtc = nowUtc;
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        string requestId,
        int attempts,
        DateTimeOffset nextAttemptAtUtc,
        string lastError,
        bool dead,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (entries.TryGetValue(requestId, out var entry))
            {
                entry.Status = dead ? "dead" : "pending";
                entry.Attempts = attempts;
                entry.NextAttemptAtUtc = nextAttemptAtUtc;
                entry.LockedBy = null;
                entry.LockedUntilUtc = null;
                entry.LastError = lastError;
                entry.UpdatedAtUtc = nowUtc;
            }
        }

        return Task.CompletedTask;
    }

    private sealed class Entry(
        GatewayUsageEvent usageEvent,
        string status,
        int attempts,
        DateTimeOffset nextAttemptAtUtc,
        string? lockedBy,
        DateTimeOffset? lockedUntilUtc,
        string? lastError,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        public GatewayUsageEvent UsageEvent { get; } = usageEvent;

        public string Status { get; set; } = status;

        public int Attempts { get; set; } = attempts;

        public DateTimeOffset NextAttemptAtUtc { get; set; } = nextAttemptAtUtc;

        public string? LockedBy { get; set; } = lockedBy;

        public DateTimeOffset? LockedUntilUtc { get; set; } = lockedUntilUtc;

        public string? LastError { get; set; } = lastError;

        public DateTimeOffset CreatedAtUtc { get; } = createdAtUtc;

        public DateTimeOffset UpdatedAtUtc { get; set; } = updatedAtUtc;
    }
}
