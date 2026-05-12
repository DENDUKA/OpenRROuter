using System.Collections.Concurrent;
using OpenRussiaRouter.BillingService.Application.Interfaces;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Application.Services;

/// <summary>
/// In-memory Billing store used before PostgreSQL persistence is added.
/// </summary>
public sealed class InMemoryBillingRepository : IBillingRepository
{
    private readonly ConcurrentDictionary<string, LedgerEntry> _ledgerByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ModelPrice> _models = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UsageRecord> _usageByRequestId = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<BillingAnomaly> _anomalies = [];

    /// <summary>
    /// Adds an idempotent ledger entry.
    /// </summary>
    /// <param name="entry">Ledger entry.</param>
    /// <returns>Existing or inserted entry.</returns>
    public LedgerEntry AddLedgerEntry(LedgerEntry entry)
    {
        return _ledgerByIdempotencyKey.GetOrAdd(entry.IdempotencyKey, entry);
    }

    /// <summary>
    /// Calculates current user balance from the append-only ledger.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <returns>Current balance.</returns>
    public MoneyAmount GetBalance(string userId)
    {
        var amount = _ledgerByIdempotencyKey.Values
            .Where(entry => entry.UserId == userId)
            .Select(entry => entry.Amount.Amount)
            .DefaultIfEmpty(0m)
            .Sum();

        return MoneyAmount.Rub(amount);
    }

    /// <summary>
    /// Upserts a model price.
    /// </summary>
    /// <param name="model">Model price.</param>
    public void UpsertModel(ModelPrice model)
    {
        _models[model.ModelId] = model;
    }

    /// <summary>
    /// Gets an enabled model price.
    /// </summary>
    /// <param name="modelId">Public model id.</param>
    /// <returns>Model price or null.</returns>
    public ModelPrice? GetEnabledModel(string modelId)
    {
        return _models.TryGetValue(modelId, out var model) && model.Enabled ? model : null;
    }

    /// <summary>
    /// Lists enabled public models.
    /// </summary>
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

    /// <summary>
    /// Adds an idempotent usage record.
    /// </summary>
    /// <param name="record">Usage record.</param>
    /// <returns>Existing or inserted record.</returns>
    public UsageRecord AddUsage(UsageRecord record)
    {
        return _usageByRequestId.GetOrAdd(record.RequestId, record);
    }

    /// <summary>
    /// Gets a usage record by request id.
    /// </summary>
    /// <param name="requestId">Request id.</param>
    /// <returns>Usage record or null.</returns>
    public UsageRecord? GetUsage(string requestId)
    {
        return _usageByRequestId.GetValueOrDefault(requestId);
    }

    /// <summary>
    /// Lists usage records.
    /// </summary>
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

    /// <summary>
    /// Adds a billing anomaly.
    /// </summary>
    /// <param name="anomaly">Billing anomaly.</param>
    public void AddAnomaly(BillingAnomaly anomaly)
    {
        _anomalies.Add(anomaly);
    }

    /// <summary>
    /// Lists billing anomalies.
    /// </summary>
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
