using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Application.Services;

/// <summary>
/// Storage abstraction for Billing ledger, catalog, usage and anomalies.
/// </summary>
public interface IBillingRepository
{
    /// <summary>
    /// Adds an idempotent ledger entry.
    /// </summary>
    /// <param name="entry">Ledger entry.</param>
    /// <returns>Existing or inserted entry.</returns>
    LedgerEntry AddLedgerEntry(LedgerEntry entry);

    /// <summary>
    /// Gets current user balance.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <returns>Balance.</returns>
    MoneyAmount GetBalance(string userId);

    /// <summary>
    /// Upserts a model price.
    /// </summary>
    /// <param name="model">Model price.</param>
    void UpsertModel(ModelPrice model);

    /// <summary>
    /// Gets an enabled model price.
    /// </summary>
    /// <param name="modelId">Public model id.</param>
    /// <returns>Model price or null.</returns>
    ModelPrice? GetEnabledModel(string modelId);

    /// <summary>
    /// Lists enabled model prices.
    /// </summary>
    /// <param name="limit">Maximum number of models to return.</param>
    /// <param name="afterModelId">Cursor: return models after this model ID.</param>
    /// <returns>Tuple of models and whether more exist.</returns>
    (IReadOnlyCollection<ModelPrice> Items, bool HasMore) ListEnabledModels(int limit, string? afterModelId = null);

    /// <summary>
    /// Lists all model prices (including disabled) for admin views.
    /// </summary>
    /// <param name="limit">Maximum number of models to return.</param>
    /// <param name="afterModelId">Cursor: return models after this model ID.</param>
    /// <returns>Tuple of models and whether more exist.</returns>
    (IReadOnlyCollection<ModelPrice> Items, bool HasMore) ListAllModels(int limit, string? afterModelId = null);

    /// <summary>
    /// Adds an idempotent usage record.
    /// </summary>
    /// <param name="record">Usage record.</param>
    /// <returns>Existing or inserted record.</returns>
    UsageRecord AddUsage(UsageRecord record);

    /// <summary>
    /// Gets usage by request id.
    /// </summary>
    /// <param name="requestId">Request id.</param>
    /// <returns>Usage record or null.</returns>
    UsageRecord? GetUsage(string requestId);

    /// <summary>
    /// Lists usage records.
    /// </summary>
    /// <param name="limit">Maximum number of records to return.</param>
    /// <param name="afterCreatedAtUtc">Cursor: return records after this timestamp.</param>
    /// <returns>Tuple of records and whether more exist.</returns>
    (IReadOnlyCollection<UsageRecord> Items, bool HasMore) ListUsage(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? userId = null);

    /// <summary>
    /// Adds a billing anomaly.
    /// </summary>
    /// <param name="anomaly">Billing anomaly.</param>
    void AddAnomaly(BillingAnomaly anomaly);

    /// <summary>
    /// Lists billing anomalies.
    /// </summary>
    /// <param name="limit">Maximum number of anomalies to return.</param>
    /// <param name="afterCreatedAtUtc">Cursor: return anomalies after this timestamp.</param>
    /// <returns>Tuple of anomalies and whether more exist.</returns>
    (IReadOnlyCollection<BillingAnomaly> Items, bool HasMore) ListAnomalies(int limit, DateTimeOffset? afterCreatedAtUtc = null);
}