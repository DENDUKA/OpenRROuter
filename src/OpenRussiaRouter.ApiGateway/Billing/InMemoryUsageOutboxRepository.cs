using System.Collections.Concurrent;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed class InMemoryUsageOutboxRepository : IUsageOutboxRepository
{
    private readonly ConcurrentDictionary<string, UsageOutboxRecord> records = new(StringComparer.Ordinal);

    public Task EnqueueAsync(GatewayUsageEvent ev, CancellationToken ct)
    {
        var record = new UsageOutboxRecord(
            ev.RequestId,
            ev.UserId,
            ev.ApiKeyId,
            ev.ApiKeyPrefix,
            ev.ModelId,
            ev.RouterNodeId,
            ev.Usage,
            ev.Status,
            ev.StartedAtUtc,
            ev.CompletedAtUtc,
            ev.ProviderName,
            ev.ProviderAccount,
            ev.Endpoint,
            ev.Stream ?? false,
            Attempts: 0,
            NextAttemptAtUtc: DateTimeOffset.UtcNow,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LeasedUntilUtc: null);

        records.TryAdd(record.RequestId, record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageOutboxRecord>> ClaimPendingAsync(
        int batchSize,
        TimeSpan lease,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var leasedUntil = nowUtc.Add(lease);
        var claimed = new List<UsageOutboxRecord>();

        foreach (var key in records.Keys.OrderBy(k => records[k].NextAttemptAtUtc).ToArray())
        {
            if (claimed.Count >= batchSize)
            {
                break;
            }

            if (!records.TryGetValue(key, out var record))
            {
                continue;
            }

            if (record.NextAttemptAtUtc > nowUtc)
            {
                continue;
            }

            if (record.LeasedUntilUtc.HasValue && record.LeasedUntilUtc.Value >= nowUtc)
            {
                continue;
            }

            var updated = record with { LeasedUntilUtc = leasedUntil };
            if (records.TryUpdate(key, updated, record))
            {
                claimed.Add(updated);
            }
        }

        return Task.FromResult<IReadOnlyList<UsageOutboxRecord>>(claimed);
    }

    public Task MarkSentAsync(string requestId, CancellationToken ct)
    {
        records.TryRemove(requestId, out _);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        string requestId,
        int attempts,
        DateTimeOffset nextAttemptUtc,
        CancellationToken ct)
    {
        if (records.TryGetValue(requestId, out var record))
        {
            records.TryUpdate(requestId, record with
            {
                Attempts = attempts,
                NextAttemptAtUtc = nextAttemptUtc,
                LeasedUntilUtc = null
            }, record);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyCollection<UsageOutboxRecord> GetAll()
    {
        return records.Values.ToArray();
    }
}
