using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Common;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;

namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class BillingApplicationService(
    IBillingRepository repository,
    IUsageCostCalculator usageCostCalculator,
    IOptions<BillingPaginationOptions> paginationOptions,
    ILogger<BillingApplicationService> logger)
{
    public async Task<LedgerEntry> CreateVirtualTopUpAsync(
        string adminId,
        string userId,
        MoneyAmount amount,
        string reason,
        string idempotencyKey,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        _ = Preconditions.RequireValue(adminId, nameof(adminId));
        ValidatePositiveRub(amount, nameof(amount));

        return await repository.AddLedgerEntryAsync(new LedgerEntry(
            Guid.NewGuid().ToString("N"),
            Preconditions.RequireValue(userId, nameof(userId)),
            LedgerEntryType.VirtualTopUp,
            amount,
            Preconditions.RequireValue(idempotencyKey, nameof(idempotencyKey)),
            Preconditions.RequireValue(reason, nameof(reason)),
            nowUtc), ct).ConfigureAwait(false);
    }

    public async Task UpsertModelAsync(ModelPrice model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        _ = Preconditions.RequireValue(model.ModelId, nameof(model.ModelId));
        _ = Preconditions.RequireValue(model.DisplayName, nameof(model.DisplayName));
        _ = Preconditions.RequireValue(model.RouterModelId, nameof(model.RouterModelId));
        _ = Preconditions.RequireValue(model.PriceVersion, nameof(model.PriceVersion));
        model.Price.EnsureValid();
        await repository.UpsertModelAsync(model, ct).ConfigureAwait(false);
    }

    public async Task<BalanceCheckResult> CheckBalanceAndModelAsync(string userId, string modelId, CancellationToken ct = default)
    {
        var balance = await repository.GetBalanceAsync(Preconditions.RequireValue(userId, nameof(userId)), ct).ConfigureAwait(false);
        var model = await repository.GetEnabledModelAsync(Preconditions.RequireValue(modelId, nameof(modelId)), ct).ConfigureAwait(false);
        if (model is null)
            return new BalanceCheckResult(false, modelId, modelId, null, balance, ErrorCodes.ModelUnavailable);

        // Free models (all prices are zero) are accessible regardless of balance.
        var isFreeModel = model.Price.InputPerMillion.Amount == 0
                       && model.Price.OutputPerMillion.Amount == 0
                       && model.Price.CachePerMillion.Amount == 0;

        if (!isFreeModel && balance.Amount <= 0)
            return new BalanceCheckResult(false, modelId, model.RouterModelId, model.PriceVersion, balance, ErrorCodes.InsufficientBalance);

        return new BalanceCheckResult(true, modelId, model.RouterModelId, model.PriceVersion, balance, null);
    }

    public async Task<IReadOnlyCollection<PublicModel>> ListPublicModelsAsync(CancellationToken ct = default)
    {
        var pageSize = paginationOptions.Value.EffectivePageSize;
        return (await repository.ListEnabledModelsAsync(pageSize, null, ct).ConfigureAwait(false))
            .Items
            .Select(model => new PublicModel(model.ModelId, model.DisplayName, model.Enabled))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<ModelPrice>> ListAllModelsAsync(CancellationToken ct = default)
    {
        var pageSize = paginationOptions.Value.EffectivePageSize;
        return (await repository.ListAllModelsAsync(pageSize, null, ct).ConfigureAwait(false)).Items;
    }

    public async Task<RecordUsageResult> RecordUsageAsync(UsageEvent usageEvent, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usageEvent);

        if (await repository.GetUsageAsync(usageEvent.RequestId, ct).ConfigureAwait(false) is { } existing)
            return new RecordUsageResult(true, existing.RequestId, existing.Cost, null);

        try
        {
            usageEvent.Usage.EnsureValid();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            logger.LogWarning(ex, "Usage record validation failed. RequestId: {RequestId}; UserId: {UserId}; ModelId: {ModelId}", usageEvent.RequestId, usageEvent.UserId, usageEvent.ModelId);
            await AddAnomalyAsync(usageEvent.RequestId, "invalid_token_count", ex.Message, nowUtc, ct).ConfigureAwait(false);
            return new RecordUsageResult(false, null, MoneyAmount.Rub(0), "invalid_token_count");
        }

        await repository.AddActivityByTokenAsync(new ActivityByTokenRecord(
            usageEvent.RequestId,
            usageEvent.ApiKeyId,
            usageEvent.ApiKeyPrefix,
            usageEvent.ModelId,
            usageEvent.RouterNodeId,
            usageEvent.Usage,
            MoneyAmount.Rub(0),
            usageEvent.Status,
            nowUtc,
            usageEvent.CompletedAtUtc,
            usageEvent.ProviderName,
            usageEvent.ProviderAccount), ct).ConfigureAwait(false);

        if (usageEvent.Usage.TotalTokens == 0)
            return new RecordUsageResult(true, usageEvent.RequestId, MoneyAmount.Rub(0), null);

        var model = await repository.GetEnabledModelAsync(usageEvent.ModelId, ct).ConfigureAwait(false);
        if (model is null)
        {
            await AddAnomalyAsync(usageEvent.RequestId, "model_price_missing", "Enabled model price was not found.", nowUtc, ct).ConfigureAwait(false);
            logger.LogError("Model price not found for model {ModelId}, request {RequestId}. Usage recorded with zero cost.", usageEvent.ModelId, usageEvent.RequestId);
            return new RecordUsageResult(true, usageEvent.RequestId, MoneyAmount.Rub(0), null);
        }

        var cost = usageCostCalculator.Calculate(usageEvent.Usage, model.Price);
        var record = await repository.AddUsageAsync(new UsageRecord(
            usageEvent.RequestId,
            usageEvent.UserId,
            usageEvent.ApiKeyId,
            usageEvent.ModelId,
            model.PriceVersion,
            usageEvent.RouterNodeId,
            usageEvent.Usage,
            cost,
            usageEvent.Status,
            nowUtc,
            usageEvent.ProviderName,
            usageEvent.ProviderAccount), ct).ConfigureAwait(false);

        await repository.AddLedgerEntryAsync(new LedgerEntry(
            Guid.NewGuid().ToString("N"),
            usageEvent.UserId,
            LedgerEntryType.UsageCharge,
            MoneyAmount.Rub(-record.Cost.Amount),
            $"usage:{usageEvent.RequestId}",
            $"Usage charge for {usageEvent.ModelId}",
            nowUtc), ct).ConfigureAwait(false);

        return new RecordUsageResult(true, record.RequestId, record.Cost, null);
    }

    public async Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default) =>
        await repository.GetBalanceAsync(Preconditions.RequireValue(userId, nameof(userId)), ct).ConfigureAwait(false);

    public async Task<IReadOnlyCollection<LedgerEntry>> ListTopUpsAsync(string userId, CancellationToken ct = default)
    {
        var pageSize = paginationOptions.Value.EffectivePageSize;
        return (await repository.ListTopUpsAsync(Preconditions.RequireValue(userId, nameof(userId)), pageSize, ct).ConfigureAwait(false)).Items;
    }

    public async Task<IReadOnlyCollection<BillingAnomaly>> ListAnomaliesAsync(CancellationToken ct = default)
    {
        var pageSize = paginationOptions.Value.EffectivePageSize;
        return (await repository.ListAnomaliesAsync(pageSize, null, ct).ConfigureAwait(false)).Items;
    }

    public async Task<IReadOnlyCollection<UsageRecord>> ListUsageAsync(string? userId = null, CancellationToken ct = default)
    {
        var pageSize = paginationOptions.Value.EffectivePageSize;
        return (await repository.ListUsageAsync(pageSize, null, userId, ct).ConfigureAwait(false)).Items;
    }

    public async Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        DateTimeOffset? afterCreatedAtUtc = null,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return (Array.Empty<ActivityByTokenRecord>(), false);

        var limit = pageSize ?? paginationOptions.Value.EffectivePageSize;
        return await repository.ListActivityByTokenAsync(
            apiKeyIds, limit, fromUtc, toUtc, modelIdFilter, apiKeyIdFilter, afterCreatedAtUtc, ct).ConfigureAwait(false);
    }

    public async Task<ActivitySummary> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return new ActivitySummary(0, 0, 0, 0, MoneyAmount.Rub(0), [], []);

        return await repository.GetActivitySummaryAsync(apiKeyIds, fromUtc, toUtc, ct).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyCollection<AdminRequestRecord> Items, bool HasMore)> GetAdminUsageRecordsAsync(
        int pageSize,
        string? afterCursor = null,
        string? modelFilter = null,
        string? providerFilter = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        var limit = pageSize > 0 ? pageSize : paginationOptions.Value.EffectivePageSize;
        return await repository.GetAdminUsageRecordsAsync(limit, afterCursor, modelFilter, providerFilter, fromUtc, toUtc, ct).ConfigureAwait(false);
    }

    private static void ValidatePositiveRub(MoneyAmount amount, string name)
    {
        if (amount.Currency != Currency.Rub)
            throw new ArgumentException("Only RUB is supported.", name);

        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(name, "Amount must be positive.");
    }

    private async Task AddAnomalyAsync(string requestId, string type, string message, DateTimeOffset nowUtc, CancellationToken ct)
    {
        await repository.AddAnomalyAsync(new BillingAnomaly(Guid.NewGuid().ToString("N"), requestId, type, message, nowUtc), ct).ConfigureAwait(false);
    }
}
