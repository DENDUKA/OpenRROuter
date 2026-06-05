using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Common;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;

namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed partial class BillingApplicationService(
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
        ValidatePositiveUsd(amount, nameof(amount));

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

    public async Task<BalanceCheckResult> CheckBalanceAndModelAsync(string userId, string modelId, bool adminBypassDisabled = false, CancellationToken ct = default)
    {
        var balance = await repository.GetBalanceAsync(Preconditions.RequireValue(userId, nameof(userId)), ct).ConfigureAwait(false);
        var resolvedModelId = Preconditions.RequireValue(modelId, nameof(modelId));
        var model = adminBypassDisabled
            ? await repository.GetModelAsync(resolvedModelId, ct).ConfigureAwait(false)
            : await repository.GetEnabledModelAsync(resolvedModelId, ct).ConfigureAwait(false);
            
        if (model is null)
            return new BalanceCheckResult(false, modelId, modelId, null, balance, ErrorCodes.ModelUnavailable);

        if (adminBypassDisabled && !model.Enabled)
        {
            logger.LogInformation(
                "admin disabled-model bypass: user={UserId} model={ModelId}",
                userId,
                modelId);
        }

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
            .Select(model => new PublicModel(model.RouterModelId, model.DisplayName, model.Enabled))
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
            return new RecordUsageResult(false, null, MoneyAmount.Usd(0), "invalid_token_count");
        }

        var cost = MoneyAmount.Usd(0);
        string? priceVersion = null;

        if (usageEvent.Usage.TotalTokens > 0)
        {
            var model = await repository.GetEnabledModelAsync(usageEvent.ModelId, ct).ConfigureAwait(false);
            if (model is null)
            {
                await AddAnomalyAsync(usageEvent.RequestId, "model_price_missing", "Enabled model price was not found.", nowUtc, ct).ConfigureAwait(false);
                logger.LogError("Model price not found for model {ModelId}, request {RequestId}. Usage recorded with zero cost.", usageEvent.ModelId, usageEvent.RequestId);
            }
            else
            {
                cost = usageCostCalculator.Calculate(usageEvent.Usage, model.Price);
                priceVersion = model.PriceVersion;
            }
        }

        // ВНИМАНИЕ: Здесь нужна единая транзакция (Unit of Work) на уровне репозитория.
        // Иначе частичные отказы сломают баланс.
        var activity = new ActivityByTokenRecord(
            usageEvent.RequestId,
            usageEvent.ApiKeyId,
            usageEvent.ApiKeyPrefix,
            usageEvent.ModelId,
            usageEvent.RouterNodeId,
            usageEvent.Usage,
            cost,
            usageEvent.Status,
            nowUtc,
            usageEvent.CompletedAtUtc,
            usageEvent.ProviderName,
            usageEvent.ProviderAccount);

        if (usageEvent.Usage.TotalTokens == 0 || priceVersion is null)
        {
            await repository.RecordUsageWithTransactionAsync(activity, null, null, ct).ConfigureAwait(false);
            return new RecordUsageResult(true, usageEvent.RequestId, cost, null);
        }

        var usage = new UsageRecord(
            usageEvent.RequestId,
            usageEvent.UserId,
            usageEvent.ApiKeyId,
            usageEvent.ModelId,
            priceVersion,
            usageEvent.RouterNodeId,
            usageEvent.Usage,
            cost,
            usageEvent.Status,
            nowUtc,
            usageEvent.ProviderName,
            usageEvent.ProviderAccount);

        var ledgerEntry = new LedgerEntry(
            Guid.NewGuid().ToString("N"),
            usageEvent.UserId,
            LedgerEntryType.UsageCharge,
            MoneyAmount.Usd(-cost.Amount),
            $"usage:{usageEvent.RequestId}",
            $"Usage charge for {usageEvent.ModelId}",
            nowUtc);

        await repository.RecordUsageWithTransactionAsync(activity, usage, ledgerEntry, ct).ConfigureAwait(false);

        return new RecordUsageResult(true, usage.RequestId, usage.Cost, null);
    }

    public async Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default) =>
        await repository.GetBalanceAsync(Preconditions.RequireValue(userId, nameof(userId)), ct).ConfigureAwait(false);

    public async Task<IReadOnlyCollection<LedgerEntry>> ListTopUpsAsync(string userId, CancellationToken ct = default)
    {
        var pageSize = paginationOptions.Value.EffectivePageSize;
        return (await repository.ListTopUpsAsync(Preconditions.RequireValue(userId, nameof(userId)), pageSize, ct).ConfigureAwait(false)).Items;
    }

    public async Task<(IReadOnlyCollection<BillingAnomaly> Items, bool HasMore)> ListAnomaliesAsync(string? afterCursor = null, CancellationToken ct = default)
    {
        ParseCursor(afterCursor, out var afterCreatedAtUtc, out var afterId);
        return await repository.ListAnomaliesAsync(paginationOptions.Value.EffectivePageSize, afterCreatedAtUtc, afterId, ct).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyCollection<UsageRecord> Items, bool HasMore)> ListUsageAsync(string? userId = null, string? afterCursor = null, CancellationToken ct = default)
    {
        ParseCursor(afterCursor, out var afterCreatedAtUtc, out var afterRequestId);
        return await repository.ListUsageAsync(paginationOptions.Value.EffectivePageSize, afterCreatedAtUtc, afterRequestId, userId, ct).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        string? afterCursor = null,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return (Array.Empty<ActivityByTokenRecord>(), false);

        var limit = pageSize ?? paginationOptions.Value.EffectivePageSize;
        ParseCursor(afterCursor, out var afterCreatedAtUtc, out var afterRequestId);

        return await repository.ListActivityByTokenAsync(
            apiKeyIds, limit, fromUtc, toUtc, modelIdFilter, apiKeyIdFilter, afterCreatedAtUtc, afterRequestId, ct).ConfigureAwait(false);
    }

    public async Task<ActivitySummary> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return new ActivitySummary(0, 0, 0, 0, MoneyAmount.Usd(0), [], []);

        return await repository.GetActivitySummaryAsync(apiKeyIds, fromUtc, toUtc, ct).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyCollection<AdminRequestRecord> Items, bool HasMore)> GetAdminUsageRecordsAsync(
        int limit,
        string? afterCursor = null,
        string? modelFilter = null,
        string? providerFilter = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        return await repository.GetAdminUsageRecordsAsync(limit, afterCursor, modelFilter, providerFilter, fromUtc, toUtc, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyCollection<Tag>> ListTagsAsync(CancellationToken ct = default)
        => repository.ListTagsAsync(ct);

    public Task UpsertTagAsync(Tag tag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tag);

        var id = Preconditions.RequireValue(tag.Id, nameof(tag.Id));
        var displayName = Preconditions.RequireValue(tag.DisplayName, nameof(tag.DisplayName));
        var color = string.IsNullOrWhiteSpace(tag.Color) ? null : tag.Color.Trim();

        if (!TagIdRegex().IsMatch(id))
            throw new ArgumentException("Tag ID must be alphanumeric with dashes, underscores, dots or slashes.", nameof(tag.Id));

        if (id.Length > 50)
            throw new ArgumentException("Tag ID cannot be longer than 50 characters.", nameof(tag.Id));

        if (displayName.Length > 100)
            throw new ArgumentException("Tag display name cannot be longer than 100 characters.", nameof(tag.DisplayName));

        if (color is not null && !HexColorRegex().IsMatch(color))
            throw new ArgumentException("Tag color must be a hex color starting with #.", nameof(tag.Color));

        return repository.UpsertTagAsync(tag with { Id = id, DisplayName = displayName, Color = color }, ct);
    }

    public Task DeleteTagAsync(string id, CancellationToken ct = default)
        => repository.DeleteTagAsync(Preconditions.RequireValue(id, nameof(id)), ct);

    private static void ValidatePositiveUsd(MoneyAmount amount, string name)
    {
        if (amount.Currency != Currency.Usd)
            throw new ArgumentException("Only $ is supported.", name);

        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(name, "Amount must be positive.");
    }

    private async Task AddAnomalyAsync(string requestId, string type, string message, DateTimeOffset nowUtc, CancellationToken ct)
    {
        await repository.AddAnomalyAsync(new BillingAnomaly(Guid.NewGuid().ToString("N"), requestId, type, message, nowUtc), ct).ConfigureAwait(false);
    }

    private static void ParseCursor(string? cursor, out DateTimeOffset? date, out string? id)
    {
        date = null;
        id = null;
        
        if (string.IsNullOrEmpty(cursor)) 
            return;

        var parts = cursor.Split('|', 2);
        if (parts.Length == 2 && DateTimeOffset.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDate))
        {
            date = parsedDate;
            id = parts[1];
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9_\-./]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TagIdRegex();

    [GeneratedRegex(@"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HexColorRegex();
}