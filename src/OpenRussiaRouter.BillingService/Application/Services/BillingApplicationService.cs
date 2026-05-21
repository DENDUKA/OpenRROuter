using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;

namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class BillingApplicationService(
    IBillingRepository repository,
    IUsageCostCalculator usageCostCalculator,
    IOptions<BillingPaginationOptions> paginationOptions,
    ILogger<BillingApplicationService>? logger = null)
{

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

    public void UpsertModel(ModelPrice model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _ = RequireValue(model.ModelId, nameof(model.ModelId));
        _ = RequireValue(model.DisplayName, nameof(model.DisplayName));
        _ = RequireValue(model.RouterModelId, nameof(model.RouterModelId));
        _ = RequireValue(model.PriceVersion, nameof(model.PriceVersion));
        model.Price.EnsureValid();
        repository.UpsertModel(model);
    }

    public BalanceCheckResult CheckBalanceAndModel(string userId, string modelId)
    {
        var balance = repository.GetBalance(RequireValue(userId, nameof(userId)));
        var model = repository.GetEnabledModel(RequireValue(modelId, nameof(modelId)));
        if (model is null)
        {
            return new BalanceCheckResult(false, modelId, modelId, null, balance, ErrorCodes.ModelUnavailable);
        }

        // Free models (all prices are zero) are accessible regardless of balance.
        var isFreeModel = model.Price.InputPerMillion.Amount == 0
                       && model.Price.OutputPerMillion.Amount == 0
                       && model.Price.CachePerMillion.Amount == 0;

        if (!isFreeModel && balance.Amount <= 0)
        {
            return new BalanceCheckResult(false, modelId, model.RouterModelId, model.PriceVersion, balance, ErrorCodes.InsufficientBalance);
        }

        return new BalanceCheckResult(true, modelId, model.RouterModelId, model.PriceVersion, balance, null);
    }

    public IReadOnlyCollection<PublicModel> ListPublicModels()
    {
        var pageSize = Math.Min(paginationOptions.Value.DefaultPageSize, paginationOptions.Value.MaxPageSize);
        return repository.ListEnabledModels(pageSize, null)
            .Items
            .Select(model => new PublicModel(model.ModelId, model.DisplayName, model.Enabled))
            .ToArray();
    }

    public IReadOnlyCollection<ModelPrice> ListAllModels()
    {
        var pageSize = Math.Min(paginationOptions.Value.DefaultPageSize, paginationOptions.Value.MaxPageSize);
        return repository.ListAllModels(pageSize, null).Items;
    }

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
            logger?.LogWarning(ex, "Usage record validation failed. RequestId: {RequestId}; UserId: {UserId}; ModelId: {ModelId}", usageEvent.RequestId, usageEvent.UserId, usageEvent.ModelId);
            AddAnomaly(usageEvent.RequestId, "invalid_token_count", ex.Message, nowUtc);
            return new RecordUsageResult(false, null, MoneyAmount.Rub(0), "invalid_token_count");
        }

        repository.AddActivityByToken(new ActivityByTokenRecord(
            usageEvent.RequestId,
            usageEvent.ApiKeyId,
            usageEvent.ApiKeyPrefix,
            usageEvent.ModelId,
            usageEvent.RouterNodeId,
            usageEvent.Usage,
            usageEvent.Status,
            nowUtc,
            usageEvent.CompletedAtUtc));

        if (usageEvent.Usage.TotalTokens == 0)
        {
            return new RecordUsageResult(true, usageEvent.RequestId, MoneyAmount.Rub(0), null);
        }

        var model = repository.GetEnabledModel(usageEvent.ModelId);
        if (model is null)
        {
            AddAnomaly(usageEvent.RequestId, "model_price_missing", "Enabled model price was not found.", nowUtc);
            return new RecordUsageResult(false, null, MoneyAmount.Rub(0), "model_price_missing");
        }

        var cost = usageCostCalculator.Calculate(usageEvent.Usage, model.Price);
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

    public MoneyAmount GetBalance(string userId) => repository.GetBalance(RequireValue(userId, nameof(userId)));

    public IReadOnlyCollection<LedgerEntry> ListTopUps(string userId)
    {
        var pageSize = Math.Min(paginationOptions.Value.DefaultPageSize, paginationOptions.Value.MaxPageSize);
        return repository.ListTopUps(RequireValue(userId, nameof(userId)), pageSize).Items;
    }

    public IReadOnlyCollection<BillingAnomaly> ListAnomalies()
    {
        var pageSize = Math.Min(paginationOptions.Value.DefaultPageSize, paginationOptions.Value.MaxPageSize);
        return repository.ListAnomalies(pageSize, null).Items;
    }

    public IReadOnlyCollection<UsageRecord> ListUsage(string? userId = null)
    {
        var pageSize = Math.Min(paginationOptions.Value.DefaultPageSize, paginationOptions.Value.MaxPageSize);
        return repository.ListUsage(pageSize, null, userId).Items;
    }

    public IReadOnlyCollection<ActivityByTokenRecord> ListActivityByToken(IReadOnlyCollection<string> apiKeyIds)
    {
        if (apiKeyIds.Count == 0)
        {
            return Array.Empty<ActivityByTokenRecord>();
        }

        var pageSize = Math.Min(paginationOptions.Value.DefaultPageSize, paginationOptions.Value.MaxPageSize);
        return repository.ListActivityByToken(apiKeyIds, pageSize).Items;
    }

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
