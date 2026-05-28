using System.Collections.Concurrent;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

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
        if (_models.TryGetValue(model.ModelId, out var existing) && model.DescriptionSource is null)
        {
            model = model with
            {
                Description = model.Description ?? existing.Description,
                ContextLength = model.ContextLength ?? existing.ContextLength,
                Modalities = model.Modalities ?? existing.Modalities,
                DescriptionSource = existing.DescriptionSource
            };
        }
        _models[model.ModelId] = model;
        return Task.CompletedTask;
    }

    public Task<ModelPrice?> GetEnabledModelAsync(string routerModelId, CancellationToken ct = default) =>
        Task.FromResult(_models.Values.FirstOrDefault(m => m.RouterModelId == routerModelId && m.Enabled));

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
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        DateTimeOffset? afterCreatedAtUtc = null,
        CancellationToken ct = default)
    {
        var keySet = apiKeyIds.ToHashSet(StringComparer.Ordinal);
        var query = _activityByRequestId.Values
            .Where(item => keySet.Contains(item.ApiKeyId));

        if (fromUtc.HasValue)
            query = query.Where(item => item.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(item => item.CreatedAtUtc <= toUtc.Value);
        if (!string.IsNullOrEmpty(modelIdFilter))
            query = query.Where(item => item.ModelId == modelIdFilter);
        if (!string.IsNullOrEmpty(apiKeyIdFilter))
            query = query.Where(item => item.ApiKeyId == apiKeyIdFilter);
        if (afterCreatedAtUtc.HasValue)
            query = query.Where(item => item.CreatedAtUtc < afterCreatedAtUtc.Value);

        var items = query
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(limit + 1)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return Task.FromResult<(IReadOnlyCollection<ActivityByTokenRecord>, bool)>((items, hasMore));
    }

    public Task<ActivitySummary> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        var keySet = apiKeyIds.ToHashSet(StringComparer.Ordinal);
        var query = _activityByRequestId.Values
            .Where(item => keySet.Contains(item.ApiKeyId));

        if (fromUtc.HasValue)
            query = query.Where(item => item.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(item => item.CreatedAtUtc <= toUtc.Value);

        var all = query.ToArray();

        var costByRequest = _usageByRequestId.Values
            .ToDictionary(u => u.RequestId, u => u.Cost.Amount, StringComparer.Ordinal);

        decimal TotalCostFor(IEnumerable<ActivityByTokenRecord> records) =>
            records.Sum(r => costByRequest.TryGetValue(r.RequestId, out var c) ? c : 0m);

        var models = all
            .GroupBy(r => r.ModelId, StringComparer.Ordinal)
            .Select(g => new ActivityModelSummary(
                g.Key,
                g.LongCount(),
                g.Sum(r => r.Usage.InputTokens),
                g.Sum(r => r.Usage.OutputTokens),
                g.Sum(r => r.Usage.CacheTokens),
                MoneyAmount.Rub(TotalCostFor(g))))
            .OrderByDescending(m => m.Requests)
            .ToList();

        var keys = all
            .GroupBy(r => r.ApiKeyId, StringComparer.Ordinal)
            .Select(g => new ActivityKeySummary(
                g.Key,
                g.First().ApiKeyPrefix,
                g.LongCount(),
                g.Sum(r => r.Usage.InputTokens),
                g.Sum(r => r.Usage.OutputTokens),
                g.Sum(r => r.Usage.CacheTokens),
                MoneyAmount.Rub(TotalCostFor(g))))
            .OrderByDescending(k => k.Requests)
            .ToList();

        var summary = new ActivitySummary(
            all.Length,
            all.Sum(r => r.Usage.InputTokens),
            all.Sum(r => r.Usage.OutputTokens),
            all.Sum(r => r.Usage.CacheTokens),
            MoneyAmount.Rub(TotalCostFor(all)),
            models,
            keys);

        return Task.FromResult(summary);
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

    public Task<(IReadOnlyCollection<AdminRequestRecord> Items, bool HasMore)> GetAdminUsageRecordsAsync(
        int limit,
        string? afterCursor = null,
        string? modelFilter = null,
        string? providerFilter = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        var costByRequest = _usageByRequestId.Values
            .ToDictionary(u => u.RequestId, u => u.Cost, StringComparer.Ordinal);
        var userByRequest = _usageByRequestId.Values
            .ToDictionary(u => u.RequestId, u => u.UserId, StringComparer.Ordinal);

        var query = _activityByRequestId.Values.AsEnumerable();

        if (afterCursor is not null && DateTimeOffset.TryParse(afterCursor, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cursor))
            query = query.Where(a => a.CreatedAtUtc < cursor);
        if (!string.IsNullOrEmpty(modelFilter))
            query = query.Where(a => a.ModelId == modelFilter);
        if (!string.IsNullOrEmpty(providerFilter))
            query = query.Where(a => a.ProviderName == providerFilter);
        if (fromUtc.HasValue)
            query = query.Where(a => a.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(a => a.CreatedAtUtc <= toUtc.Value);

        var items = query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(limit + 1)
            .Select(a => new AdminRequestRecord(
                a.RequestId,
                userByRequest.GetValueOrDefault(a.RequestId, ""),
                a.ApiKeyPrefix,
                a.ModelId,
                a.RouterNodeId,
                a.ProviderName,
                a.ProviderAccount,
                a.Usage,
                costByRequest.GetValueOrDefault(a.RequestId, MoneyAmount.Rub(0)),
                a.Status,
                a.CreatedAtUtc,
                a.CompletedAtUtc))
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return Task.FromResult<(IReadOnlyCollection<AdminRequestRecord>, bool)>((items, hasMore));
    }
}
