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
            .ThenByDescending(e => e.Id)
            .Take(limit + 1)
            .ToList();
        var hasMore = items.Count > limit;
        if (hasMore) items = items.Take(limit).ToList();
        return Task.FromResult(((IReadOnlyCollection<LedgerEntry>)items, hasMore));
    }

    public Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default)
    {
        var amount = _ledgerByIdempotencyKey.Values
            .Where(entry => entry.UserId == userId)
            .Select(entry => entry.Amount.Amount)
            .DefaultIfEmpty(0m)
            .Sum();
        return Task.FromResult(MoneyAmount.Usd(amount));
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

    public Task<ModelPrice?> GetModelAsync(string routerModelId, CancellationToken ct = default) =>
        Task.FromResult(_models.Values.FirstOrDefault(m => m.RouterModelId == routerModelId));

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

    public Task<(IReadOnlyCollection<UsageRecord> Items, bool HasMore)> ListUsageAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? afterRequestId = null, string? userId = null, CancellationToken ct = default)
    {
        var query = _usageByRequestId.Values.AsEnumerable();
        if (userId != null)
        {
            query = query.Where(u => u.UserId == userId);
        }

        query = query.OrderByDescending(u => u.CreatedAtUtc).ThenByDescending(u => u.RequestId);

        if (afterCreatedAtUtc.HasValue && !string.IsNullOrEmpty(afterRequestId))
        {
            query = query.Where(u => u.CreatedAtUtc < afterCreatedAtUtc.Value || (u.CreatedAtUtc == afterCreatedAtUtc.Value && string.Compare(u.RequestId, afterRequestId, StringComparison.Ordinal) < 0));
        }
        else if (afterCreatedAtUtc.HasValue)
        {
            query = query.Where(u => u.CreatedAtUtc < afterCreatedAtUtc.Value);
        }

        var items = query.Take(limit + 1).ToList();
        var hasMore = items.Count > limit;
        if (hasMore) items = items.Take(limit).ToList();
        return Task.FromResult(((IReadOnlyCollection<UsageRecord>)items, hasMore));
    }

    public Task<ActivityByTokenRecord> AddActivityByTokenAsync(ActivityByTokenRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Task.FromResult(_activityByRequestId.GetOrAdd(record.RequestId, record));
    }

    public Task RecordUsageWithTransactionAsync(ActivityByTokenRecord activity, UsageRecord? usage, LedgerEntry? ledgerEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        _activityByRequestId.TryAdd(activity.RequestId, activity);

        if (usage is not null)
        {
            _usageByRequestId.TryAdd(usage.RequestId, usage);
        }

        if (ledgerEntry is not null)
        {
            _ledgerByIdempotencyKey.TryAdd(ledgerEntry.IdempotencyKey, ledgerEntry);
        }

        return Task.CompletedTask;
    }

    public Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        DateTimeOffset? afterCreatedAtUtc = null,
        string? afterRequestId = null,
        CancellationToken ct = default)
    {
        var keySet = new HashSet<string>(apiKeyIds);

        var query = _activityByRequestId.Values.Where(a => keySet.Contains(a.ApiKeyId));

        if (fromUtc.HasValue) query = query.Where(a => a.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(a => a.CreatedAtUtc <= toUtc.Value);
        if (modelIdFilter != null) query = query.Where(a => a.ModelId == modelIdFilter);
        if (apiKeyIdFilter != null) query = query.Where(a => a.ApiKeyId == apiKeyIdFilter);

        query = query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.RequestId);

        if (afterCreatedAtUtc.HasValue && !string.IsNullOrEmpty(afterRequestId))
        {
            query = query.Where(a => a.CreatedAtUtc < afterCreatedAtUtc.Value || (a.CreatedAtUtc == afterCreatedAtUtc.Value && string.Compare(a.RequestId, afterRequestId, StringComparison.Ordinal) < 0));
        }
        else if (afterCreatedAtUtc.HasValue)
        {
            query = query.Where(a => a.CreatedAtUtc < afterCreatedAtUtc.Value);
        }

        var items = query.Take(limit + 1).ToList();
        var hasMore = items.Count > limit;
        if (hasMore) items = items.Take(limit).ToList();
        return Task.FromResult(((IReadOnlyCollection<ActivityByTokenRecord>)items, hasMore));
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
                MoneyAmount.Usd(TotalCostFor(g))))
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
                MoneyAmount.Usd(TotalCostFor(g))))
            .OrderByDescending(k => k.Requests)
            .ToList();

        var summary = new ActivitySummary(
            all.Length,
            all.Sum(r => r.Usage.InputTokens),
            all.Sum(r => r.Usage.OutputTokens),
            all.Sum(r => r.Usage.CacheTokens),
            MoneyAmount.Usd(TotalCostFor(all)),
            models,
            keys);

        return Task.FromResult(summary);
    }

    public Task AddAnomalyAsync(BillingAnomaly anomaly, CancellationToken ct = default)
    {
        _anomalies.Add(anomaly);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyCollection<BillingAnomaly> Items, bool HasMore)> ListAnomaliesAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? afterId = null, CancellationToken ct = default)
    {
        var query = _anomalies.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id).AsEnumerable();

        if (afterCreatedAtUtc.HasValue && !string.IsNullOrEmpty(afterId))
        {
            query = query.Where(a => a.CreatedAtUtc < afterCreatedAtUtc.Value || (a.CreatedAtUtc == afterCreatedAtUtc.Value && string.Compare(a.Id, afterId, StringComparison.Ordinal) < 0));
        }
        else if (afterCreatedAtUtc.HasValue)
        {
            query = query.Where(a => a.CreatedAtUtc < afterCreatedAtUtc.Value);
        }

        var items = query.Take(limit + 1).ToList();
        var hasMore = items.Count > limit;
        if (hasMore) items = items.Take(limit).ToList();
        return Task.FromResult(((IReadOnlyCollection<BillingAnomaly>)items, hasMore));
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
        var query = _activityByRequestId.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(modelFilter))
            query = query.Where(a => a.ModelId == modelFilter);
        if (!string.IsNullOrEmpty(providerFilter))
            query = query.Where(a => a.ProviderName == providerFilter);
        if (fromUtc.HasValue)
            query = query.Where(a => a.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(a => a.CreatedAtUtc <= toUtc.Value);

        query = query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.RequestId);

        if (!string.IsNullOrEmpty(afterCursor))
        {
            var cursorTime = DateTimeOffset.Parse(afterCursor, null, System.Globalization.DateTimeStyles.RoundtripKind);
            query = query.Where(a => a.CreatedAtUtc < cursorTime);
        }

        var results = query.Take(limit + 1).ToList();
        var hasMore = results.Count > limit;
        if (hasMore) results.RemoveAt(limit);

        var adminRecords = results.Select(a =>
        {
            _usageByRequestId.TryGetValue(a.RequestId, out var usage);
            return new AdminRequestRecord(
                a.RequestId,
                usage?.UserId ?? string.Empty,
                a.ApiKeyPrefix,
                a.ModelId,
                a.RouterNodeId,
                a.ProviderName,
                a.ProviderAccount,
                a.Usage,
                usage?.Cost ?? MoneyAmount.Usd(0),
                a.Status,
                a.CreatedAtUtc,
                a.CompletedAtUtc);
        }).ToArray();

        return Task.FromResult(((IReadOnlyCollection<AdminRequestRecord>)adminRecords, hasMore));
    }

    private readonly Dictionary<string, Tag> _tags = new(StringComparer.Ordinal);

    public Task<IReadOnlyCollection<Tag>> ListTagsAsync(CancellationToken ct = default)
    {
        IReadOnlyCollection<Tag> tags = _tags.Values.OrderBy(t => t.DisplayName, StringComparer.Ordinal).ToList();
        return Task.FromResult(tags);
    }

    public Task UpsertTagAsync(Tag tag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _tags[tag.Id] = tag;
        return Task.CompletedTask;
    }

    public Task DeleteTagAsync(string id, CancellationToken ct = default)
    {
        _tags.Remove(id);
        foreach (var model in _models.Values)
        {
            if (model.TagIds?.Contains(id, StringComparer.Ordinal) != true)
            {
                continue;
            }

            _models[model.ModelId] = model with
            {
                TagIds = model.TagIds.Where(tagId => !string.Equals(tagId, id, StringComparison.Ordinal)).ToArray()
            };
        }

        return Task.CompletedTask;
    }
}
