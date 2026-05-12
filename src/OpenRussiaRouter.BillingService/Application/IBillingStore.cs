using OpenRussiaRouter.BillingService.Domain;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Application;

/// <summary>
/// Storage abstraction for Billing ledger, catalog, usage and anomalies.
/// </summary>
public interface IBillingStore
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
    /// <returns>Enabled models.</returns>
    IReadOnlyCollection<ModelPrice> ListEnabledModels();

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
    /// Adds a billing anomaly.
    /// </summary>
    /// <param name="anomaly">Billing anomaly.</param>
    void AddAnomaly(BillingAnomaly anomaly);

    /// <summary>
    /// Lists billing anomalies.
    /// </summary>
    /// <returns>Anomalies.</returns>
    IReadOnlyCollection<BillingAnomaly> ListAnomalies();
}
