using System.Collections.Concurrent;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class InMemoryBillingRepository : IBillingRepository
{
    private readonly ConcurrentDictionary<string, LedgerEntry> _ledgerByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ModelPrice> _models = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UsageRecord> _usageByRequestId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ActivityByTokenRecord> _activityByRequestId = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<BillingAnomaly> _anomalies = [];

    public Task<LedgerEntry> AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default) =>
        Task.FromResult(_ledgerByIdempotencyKey.GetOrAdd(entry.IdempotencyKey, entry));

    public Task<(IReadOnlyCollection<LedgerEntry> Items, bool HasMore)> ListTopUpsAsync(string userId, int limit, CancellationToken ct = default)
    {
        var items = _ledgerByIdempotencyKey.Values
            .Where(e => e.UserId == userId && e.Type == LedgerEntryType.VirtualTopUp)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit + 1)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return Task.FromResult<(IReadOnlyCollection<LedgerEntry>, bool)>((items, hasMore));
    }

    public Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default)
    {
        var amount = _ledgerByIdempotencyKey.Values
            .Where(entry => entry.UserId == userId)
            .Select(entry => entry.Amount.Amount)
            .DefaultIfEmpty(0m)
            .Sum();
        return Task.FromResult(MoneyAmount.Rub(amount));
    }

    public Task UpsertModelAsync(ModelPrice model, CancellationToken ct = default)
    {
        _models[model.ModelId] = model;
        return Task.CompletedTask;
    }

    public Task<ModelPrice?> GetEnabledModelAsync(string modelId, CancellationToken ct = default) =>
        Task.FromResult(_models.TryGetValue(modelId, out var model) && model.Enabled ? model : null);

    public Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListEnabledModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default)
    {
        var ordered = _models.Values
            .Where(m => m.Enabled)
            .OrderBy(m => m.ModelId)
            .AsEnumerable();

        if (afterModelId != null)
            ordered = ordered.Where(m => string.Compare(m.ModelId, afterModelId, StringComparison.Ordinal) > 0);

        var items = ordered.Take(limit + 1).ToArray();
        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return Task.FromResult<(IReadOnlyCollection<ModelPrice>, bool)>((items, hasMore));
    }

    public Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListAllModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default)
    {
        var ordered = _models.Values
            .OrderBy(m => m.ModelId)
            .AsEnumerable();

        if (afterModelId != null)
            ordered = ordered.Where(m => string.Compare(m.ModelId, afterModelId, StringComparison.Ordinal) > 0);

        var items = ordered.Take(limit + 1).ToArray();
        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return Task.FromResult<(IReadOnlyCollection<ModelPrice>, bool)>((items, hasMore));
    }

    public Task<UsageRecord> AddUsageAsync(UsageRecord record, CancellationToken ct = default) =>
        Task.FromResult(_usageByRequestId.GetOrAdd(record.RequestId, record));

    public Task<UsageRecord?> GetUsageAsync(string requestId, CancellationToken ct = default) =>
        Task.FromResult(_usageByRequestId.GetValueOrDefault(requestId));

    public Task<(IReadOnlyCollection<UsageRecord> Items, bool HasMore)> ListUsageAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? userId = null, CancellationToken ct = default)
    {
        var query = _usageByRequestId.Values.AsEnumerable();
        if (afterCreatedAtUtc.HasValue)
            query = query.Where(u => u.CreatedAtUtc > afterCreatedAtUtc.Value);
        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(u => u.UserId == userId);

        var items = query.OrderBy(u => u.CreatedAtUtc).Take(limit + 1).ToArray();
        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return Task.FromResult<(IReadOnlyCollection<UsageRecord>, bool)>((items, hasMore));
    }

    public Task<ActivityByTokenRecord> AddActivityByTokenAsync(ActivityByTokenRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Task.FromResult(_activityByRequestId.GetOrAdd(record.RequestId, record));
    }

    public Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? afterCreatedAtUtc = null,
        CancellationToken ct = default)
    {
        var keySet = apiKeyIds.ToHashSet(StringComparer.Ordinal);
        var items = _activityByRequestId.Values
            .Where(item => keySet.Contains(item.ApiKeyId))
            .Where(item => afterCreatedAtUtc is null || item.CreatedAtUtc > afterCreatedAtUtc)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(limit + 1)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return Task.FromResult<(IReadOnlyCollection<ActivityByTokenRecord>, bool)>((items, hasMore));
    }

    public Task AddAnomalyAsync(BillingAnomaly anomaly, CancellationToken ct = default)
    {
        _anomalies.Add(anomaly);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyCollection<BillingAnomaly> Items, bool HasMore)> ListAnomaliesAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, CancellationToken ct = default)
    {
        var query = _anomalies.AsEnumerable();
        if (afterCreatedAtUtc.HasValue)
            query = query.Where(a => a.CreatedAtUtc > afterCreatedAtUtc.Value);

        var items = query.OrderBy(a => a.CreatedAtUtc).Take(limit + 1).ToArray();
        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return Task.FromResult<(IReadOnlyCollection<BillingAnomaly>, bool)>((items, hasMore));
    }
}
