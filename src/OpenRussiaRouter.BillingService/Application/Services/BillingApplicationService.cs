using OpenRussiaRouter.BillingService.Application.Interfaces;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;

namespace OpenRussiaRouter.BillingService.Application.Services;

/// <summary>
/// Handles Billing MVP operations: top-up, balance checks, model prices and usage charges.
/// </summary>
public sealed class BillingApplicationService(IBillingRepository repository)
{
    /// <summary>
    /// Creates an idempotent virtual top-up.
    /// </summary>
    /// <param name="adminId">Admin user id.</param>
    /// <param name="userId">User id.</param>
    /// <param name="amount">Positive RUB amount.</param>
    /// <param name="reason">Reason shown in admin audit.</param>
    /// <param name="idempotencyKey">Idempotency key.</param>
    /// <param name="nowUtc">Creation timestamp.</param>
    /// <returns>Ledger entry.</returns>
    public LedgerEntry CreateVirtualTopUp(
        string adminId,
        string userId,
        MoneyAmount amount,
        string reason,
        string idempotencyKey,
        DateTimeOffset nowUtc)
    {
        _ = RequireValue(adminId, nameof(adminId));
        ValidatePositiveRub(amount, nameof(amount));

        return repository.AddLedgerEntry(new LedgerEntry(
            Guid.NewGuid().ToString("N"),
            RequireValue(userId, nameof(userId)),
            LedgerEntryType.VirtualTopUp,
            amount,
            RequireValue(idempotencyKey, nameof(idempotencyKey)),
            RequireValue(reason, nameof(reason)),
            nowUtc));
    }

    /// <summary>
    /// Upserts a public model price.
    /// </summary>
    /// <param name="model">Model price.</param>
    public void UpsertModel(ModelPrice model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Price.EnsureValid();
        repository.UpsertModel(model);
    }

    /// <summary>
    /// Checks whether Gateway may start a request for a user and model.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="modelId">Public model id.</param>
    /// <returns>Balance/model check result.</returns>
    public BalanceCheckResult CheckBalanceAndModel(string userId, string modelId)
    {
        var balance = repository.GetBalance(RequireValue(userId, nameof(userId)));
        var model = repository.GetEnabledModel(RequireValue(modelId, nameof(modelId)));
        if (model is null)
        {
            return new BalanceCheckResult(false, modelId, null, balance, ErrorCodes.ModelUnavailable);
        }

        if (balance.Amount <= 0)
        {
            return new BalanceCheckResult(false, modelId, model.PriceVersion, balance, ErrorCodes.InsufficientBalance);
        }

        return new BalanceCheckResult(true, modelId, model.PriceVersion, balance, null);
    }

    /// <summary>
    /// Lists enabled public models.
    /// </summary>
    /// <returns>Public model list.</returns>
    public IReadOnlyCollection<PublicModel> ListPublicModels()
    {
        return repository.ListEnabledModels(1000, null)
            .Items
            .Select(model => new PublicModel(model.ModelId, model.DisplayName, model.Enabled))
            .ToArray();
    }

    /// <summary>
    /// Records usage and creates an idempotent charge.
    /// </summary>
    /// <param name="usageEvent">Usage event from Gateway.</param>
    /// <param name="nowUtc">Creation timestamp.</param>
    /// <returns>Usage recording result.</returns>
    public RecordUsageResult RecordUsage(UsageEvent usageEvent, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(usageEvent);

        if (repository.GetUsage(usageEvent.RequestId) is { } existing)
        {
            return new RecordUsageResult(true, existing.RequestId, existing.Cost, null);
        }

        try
        {
            usageEvent.Usage.EnsureValid();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            AddAnomaly(usageEvent.RequestId, "invalid_token_count", ex.Message, nowUtc);
            return new RecordUsageResult(false, null, MoneyAmount.Rub(0), "invalid_token_count");
        }

        var model = repository.GetEnabledModel(usageEvent.ModelId);
        if (model is null)
        {
            AddAnomaly(usageEvent.RequestId, "model_price_missing", "Enabled model price was not found.", nowUtc);
            return new RecordUsageResult(false, null, MoneyAmount.Rub(0), "model_price_missing");
        }

        var cost = UsageCostCalculator.Calculate(usageEvent.Usage, model.Price);
        var record = repository.AddUsage(new UsageRecord(
            usageEvent.RequestId,
            usageEvent.UserId,
            usageEvent.ApiKeyId,
            usageEvent.ModelId,
            model.PriceVersion,
            usageEvent.RouterNodeId,
            usageEvent.Usage,
            cost,
            usageEvent.Status,
            nowUtc));

        repository.AddLedgerEntry(new LedgerEntry(
            Guid.NewGuid().ToString("N"),
            usageEvent.UserId,
            LedgerEntryType.UsageCharge,
            MoneyAmount.Rub(-record.Cost.Amount),
            $"usage:{usageEvent.RequestId}",
            $"Usage charge for {usageEvent.ModelId}",
            nowUtc));

        return new RecordUsageResult(true, record.RequestId, record.Cost, null);
    }

    /// <summary>
    /// Gets current balance.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <returns>Current balance.</returns>
    public MoneyAmount GetBalance(string userId) => repository.GetBalance(RequireValue(userId, nameof(userId)));

    /// <summary>
    /// Lists billing anomalies.
    /// </summary>
    /// <returns>Billing anomalies.</returns>
    public IReadOnlyCollection<BillingAnomaly> ListAnomalies() => repository.ListAnomalies(1000, null).Items;

/// <summary>
    /// Lists usage records for admin views.
    /// </summary>
    /// <param name="userId">Filter by user id (null for all users).</param>
    /// <returns>Usage records.</returns>
    public IReadOnlyCollection<UsageRecord> ListUsage(string? userId = null) => repository.ListUsage(1000, null, userId).Items;

    private static void ValidatePositiveRub(MoneyAmount amount, string name)
    {
        if (amount.Currency != Currency.Rub)
        {
            throw new ArgumentException("Only RUB is supported.", name);
        }

        if (amount.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Amount must be positive.");
        }
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", name);
        }

        return value.Trim();
    }

    private void AddAnomaly(string requestId, string type, string message, DateTimeOffset nowUtc)
    {
        repository.AddAnomaly(new BillingAnomaly(Guid.NewGuid().ToString("N"), requestId, type, message, nowUtc));
    }
}

/// <summary>
/// Result of usage recording.
/// </summary>
/// <param name="Accepted">Whether usage was accepted.</param>
/// <param name="RequestId">Accepted request id.</param>
/// <param name="Cost">Calculated cost.</param>
/// <param name="ErrorCode">Machine-readable error code.</param>
public sealed record RecordUsageResult(bool Accepted, string? RequestId, MoneyAmount Cost, string? ErrorCode);
