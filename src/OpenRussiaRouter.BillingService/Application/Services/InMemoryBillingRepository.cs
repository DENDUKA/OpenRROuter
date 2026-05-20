using System.Collections.Concurrent;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class InMemoryBillingRepository : IBillingRepository
{
    private readonly ConcurrentDictionary<string, LedgerEntry> _ledgerByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ModelPrice> _models = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UsageRecord> _usageByRequestId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ActivityByTokenRecord> _activityByRequestId = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<BillingAnomaly> _anomalies = [];

    public LedgerEntry AddLedgerEntry(LedgerEntry entry)
    {
        return _ledgerByIdempotencyKey.GetOrAdd(entry.IdempotencyKey, entry);
    }

    public MoneyAmount GetBalance(string userId)
    {
        var amount = _ledgerByIdempotencyKey.Values
            .Where(entry => entry.UserId == userId)
            .Select(entry => entry.Amount.Amount)
            .DefaultIfEmpty(0m)
            .Sum();

        return MoneyAmount.Rub(amount);
    }

    public void UpsertModel(ModelPrice model)
    {
        _models[model.ModelId] = model;
    }

    public ModelPrice? GetEnabledModel(string modelId)
    {
        return _models.TryGetValue(modelId, out var model) && model.Enabled ? model : null;
    }

    public (IReadOnlyCollection<ModelPrice> Items, bool HasMore) ListEnabledModels(int limit, string? afterModelId = null)
    {
        var ordered = _models.Values
            .Where(model => model.Enabled)
            .OrderBy(model => model.ModelId)
            .AsEnumerable();

        if (afterModelId != null)
        {
            ordered = ordered.Where(model => string.Compare(model.ModelId, afterModelId, StringComparison.Ordinal) > 0);
        }

        var items = ordered.Take(limit + 1).ToArray();
        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    public (IReadOnlyCollection<ModelPrice> Items, bool HasMore) ListAllModels(int limit, string? afterModelId = null)
    {
        var ordered = _models.Values
            .OrderBy(model => model.ModelId)
            .AsEnumerable();

        if (afterModelId != null)
        {
            ordered = ordered.Where(model => string.Compare(model.ModelId, afterModelId, StringComparison.Ordinal) > 0);
        }

        var items = ordered.Take(limit + 1).ToArray();
        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    public UsageRecord AddUsage(UsageRecord record)
    {
        return _usageByRequestId.GetOrAdd(record.RequestId, record);
    }

    public UsageRecord? GetUsage(string requestId)
    {
        return _usageByRequestId.GetValueOrDefault(requestId);
    }

    public (IReadOnlyCollection<UsageRecord> Items, bool HasMore) ListUsage(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? userId = null)
    {
var query = _usageByRequestId.Values.AsEnumerable();
        if (afterCreatedAtUtc.HasValue)
        {
            query = query.Where(usage => usage.CreatedAtUtc > afterCreatedAtUtc.Value);
        }
        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(usage => usage.UserId == userId);
        }

        var items = query
            .OrderBy(usage => usage.CreatedAtUtc)
            .Take(limit + 1)
            .ToArray();
        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    public ActivityByTokenRecord AddActivityByToken(ActivityByTokenRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return _activityByRequestId.GetOrAdd(record.RequestId, record);
    }

    public (IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore) ListActivityByToken(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? afterCreatedAtUtc = null)
    {
        var keySet = apiKeyIds.ToHashSet(StringComparer.Ordinal);
        var items = _activityByRequestId.Values
            .Where(item => keySet.Contains(item.ApiKeyId))
            .Where(item => afterCreatedAtUtc is null || item.CreatedAtUtc > afterCreatedAtUtc)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(limit + 1)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    public void AddAnomaly(BillingAnomaly anomaly)
    {
        _anomalies.Add(anomaly);
    }

    public (IReadOnlyCollection<BillingAnomaly> Items, bool HasMore) ListAnomalies(int limit, DateTimeOffset? afterCreatedAtUtc = null)
    {
        var query = _anomalies.AsEnumerable();

        if (afterCreatedAtUtc.HasValue)
        {
            query = query.Where(a => a.CreatedAtUtc > afterCreatedAtUtc.Value);
        }

        var items = query
            .OrderBy(a => a.CreatedAtUtc)
            .Take(limit + 1)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }
}
