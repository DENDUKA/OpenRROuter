using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Analytics;
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
    ILogger<BillingApplicationService> logger,
    FxQuoteService? fxQuoteService = null,
    IOptions<PaymentOptions>? paymentOptions = null,
    IProductAnalytics? productAnalytics = null)
{
    public IBillingRepository Repository => repository;
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

        var allowedReasoningEfforts = ReasoningEffort.NormalizeList(model.AllowedReasoningEfforts);
        var defaultReasoningEffort = ReasoningEffort.NormalizeNullable(model.DefaultReasoningEffort);
        var providerId = string.IsNullOrWhiteSpace(model.ProviderId) ? null : model.ProviderId.Trim();
        ModelProvider? provider = null;

        if (providerId is not null)
        {
            provider = await repository.GetModelProviderAsync(providerId, ct).ConfigureAwait(false)
                ?? throw new ArgumentException($"Provider '{providerId}' was not found.", nameof(model.ProviderId));
        }

        foreach (var effort in allowedReasoningEfforts)
        {
            if (!ReasoningEffort.IsCanonical(effort))
                throw new ArgumentException($"Unsupported reasoning effort '{effort}'.", nameof(model.AllowedReasoningEfforts));
        }

        if (!model.SupportsReasoning)
        {
            if (allowedReasoningEfforts.Count > 0 || defaultReasoningEffort is not null)
                throw new ArgumentException("Reasoning efforts/default are only allowed when SupportsReasoning=true.", nameof(model));
        }
        else if (defaultReasoningEffort is not null && !allowedReasoningEfforts.Contains(defaultReasoningEffort, StringComparer.Ordinal))
        {
            throw new ArgumentException("DefaultReasoningEffort must be one of AllowedReasoningEfforts.", nameof(model.DefaultReasoningEffort));
        }

        model = model with
        {
            AllowedReasoningEfforts = allowedReasoningEfforts.Count == 0 ? null : allowedReasoningEfforts,
            DefaultReasoningEffort = defaultReasoningEffort,
            ProviderId = providerId,
            Provider = provider?.DisplayName ?? model.Provider
        };

        await repository.UpsertModelAsync(model, ct).ConfigureAwait(false);
    }

    public Task<ModelPrice?> GetModelAsync(string modelId, CancellationToken ct = default) =>
        repository.GetModelAsync(Preconditions.RequireValue(modelId, nameof(modelId)), ct);

    public async Task<BalanceCheckResult> CheckBalanceAndModelAsync(
        string userId,
        string modelId,
        bool adminBypassEnabled = false,
        string? apiKeyId = null,
        DateTimeOffset? nowUtc = null,
        CancellationToken ct = default)
    {
        var balance = await repository.GetBalanceAsync(Preconditions.RequireValue(userId, nameof(userId)), ct).ConfigureAwait(false);
        var resolvedModelId = Preconditions.RequireValue(modelId, nameof(modelId));
        var model = adminBypassEnabled
            ? await repository.GetModelAsync(resolvedModelId, ct).ConfigureAwait(false)
            : await repository.GetEnabledModelAsync(resolvedModelId, ct).ConfigureAwait(false);
            
        if (model is null)
            return new BalanceCheckResult(false, modelId, modelId, null, balance, ErrorCodes.ModelUnavailable);

        if (adminBypassEnabled && !model.Enabled)
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
            return new BalanceCheckResult(
                false,
                modelId,
                model.RouterModelId,
                model.PriceVersion,
                balance,
                ErrorCodes.InsufficientBalance,
                model.SupportsReasoning,
                model.AllowedReasoningEfforts,
                model.DefaultReasoningEffort);

        if (!isFreeModel && !string.IsNullOrWhiteSpace(apiKeyId))
        {
            var summaries = await ListApiKeyLimitSummariesAsync(
                userId,
                [apiKeyId],
                nowUtc ?? DateTimeOffset.UtcNow,
                ct).ConfigureAwait(false);

            var limitSummary = summaries.FirstOrDefault();
            if (limitSummary is { DailyExceeded: true } or { WeeklyExceeded: true })
            {
                return new BalanceCheckResult(
                    false,
                    modelId,
                    model.RouterModelId,
                    model.PriceVersion,
                    balance,
                    ErrorCodes.ApiKeyLimitExceeded,
                    model.SupportsReasoning,
                    model.AllowedReasoningEfforts,
                    model.DefaultReasoningEffort);
            }
        }

        return new BalanceCheckResult(
            true,
            modelId,
            model.RouterModelId,
            model.PriceVersion,
            balance,
            null,
            model.SupportsReasoning,
            model.AllowedReasoningEfforts,
            model.DefaultReasoningEffort);
    }

    public async Task<IReadOnlyCollection<PublicModel>> ListPublicModelsAsync(CancellationToken ct = default)
    {
        var pageSize = paginationOptions.Value.EffectivePageSize;
        return (await repository.ListEnabledModelsAsync(pageSize, null, ct).ConfigureAwait(false))
            .Items
            .Select(model => new PublicModel(
                model.ModelId,
                model.DisplayName,
                model.Enabled,
                model.SupportsReasoning,
                model.AllowedReasoningEfforts,
                model.DefaultReasoningEffort))
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
        {
            EmitApiRequestCompleted(usageEvent);
            return new RecordUsageResult(true, existing.RequestId, existing.Cost, null);
        }

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
            var zeroCostResult = new RecordUsageResult(true, usageEvent.RequestId, cost, null);
            EmitApiRequestCompleted(usageEvent);
            return zeroCostResult;
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

        var usageResult = new RecordUsageResult(true, usage.RequestId, usage.Cost, null);
        EmitApiRequestCompleted(usageEvent);
        return usageResult;
    }

    public async Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default) =>
        await repository.GetBalanceAsync(Preconditions.RequireValue(userId, nameof(userId)), ct).ConfigureAwait(false);

    public Task<ApiKeySpendLimit?> GetApiKeySpendLimitAsync(string userId, string apiKeyId, CancellationToken ct = default) =>
        repository.GetApiKeySpendLimitAsync(
            Preconditions.RequireValue(userId, nameof(userId)),
            Preconditions.RequireValue(apiKeyId, nameof(apiKeyId)),
            ct);

    public async Task UpsertApiKeySpendLimitAsync(
        string userId,
        string apiKeyId,
        MoneyAmount? dailyLimit,
        MoneyAmount? weeklyLimit,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        userId = Preconditions.RequireValue(userId, nameof(userId));
        apiKeyId = Preconditions.RequireValue(apiKeyId, nameof(apiKeyId));
        ValidateOptionalPositiveUsd(dailyLimit, nameof(dailyLimit));
        ValidateOptionalPositiveUsd(weeklyLimit, nameof(weeklyLimit));

        if (!dailyLimit.HasValue && !weeklyLimit.HasValue)
        {
            await repository.ClearApiKeySpendLimitAsync(userId, apiKeyId, ct).ConfigureAwait(false);
            return;
        }

        var existing = await repository.GetApiKeySpendLimitAsync(userId, apiKeyId, ct).ConfigureAwait(false);
        var createdAtUtc = existing?.CreatedAtUtc ?? nowUtc;
        await repository.UpsertApiKeySpendLimitAsync(
            new ApiKeySpendLimit(userId, apiKeyId, dailyLimit, weeklyLimit, createdAtUtc, nowUtc),
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<ApiKeyLimitSummary>> ListApiKeyLimitSummariesAsync(
        string userId,
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        userId = Preconditions.RequireValue(userId, nameof(userId));
        var ids = apiKeyIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return Array.Empty<ApiKeyLimitSummary>();

        return await repository.ListApiKeyLimitSummariesAsync(
            userId,
            ids,
            GetUtcDayStart(nowUtc),
            GetUtcWeekStart(nowUtc),
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<LedgerEntry>> ListTopUpsAsync(string userId, CancellationToken ct = default)
    {
        var pageSize = paginationOptions.Value.EffectivePageSize;
        return (await repository.ListTopUpsAsync(Preconditions.RequireValue(userId, nameof(userId)), pageSize, ct).ConfigureAwait(false)).Items;
    }

    public async Task<(IReadOnlyCollection<LedgerEntry> Items, int TotalCount)> ListAllTopUpsAsync(
        int pageNumber, int pageSize,
        IReadOnlyList<string>? userIds = null,
        decimal? minAmountUsd = null,
        decimal? maxAmountUsd = null,
        CancellationToken ct = default)
    {
        var limit = Math.Min(pageSize, paginationOptions.Value.MaxPageSize);
        var offset = Math.Max(0, pageNumber) * limit;
        return await repository.ListAllTopUpsAsync(offset, limit, userIds, minAmountUsd, maxAmountUsd, ct).ConfigureAwait(false);
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

    public async Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore, int TotalCount)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        string? afterCursor = null,
        int? pageSize = null,
        int offset = 0,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return (Array.Empty<ActivityByTokenRecord>(), false, 0);

        var limit = pageSize ?? paginationOptions.Value.EffectivePageSize;
        ParseCursor(afterCursor, out var afterCreatedAtUtc, out var afterRequestId);

        return await repository.ListActivityByTokenAsync(
            apiKeyIds, limit, fromUtc, toUtc, modelIdFilter, apiKeyIdFilter, afterCreatedAtUtc, afterRequestId, offset, ct).ConfigureAwait(false);
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
        IReadOnlyList<string>? modelFilters = null,
        IReadOnlyList<string>? providerFilters = null,
        IReadOnlyList<string>? apiKeyFilters = null,
        IReadOnlyList<string>? nodeFilters = null,
        IReadOnlyList<string>? accountFilters = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        return await repository.GetAdminUsageRecordsAsync(limit, afterCursor, modelFilters, providerFilters, apiKeyFilters, nodeFilters, accountFilters, fromUtc, toUtc, ct).ConfigureAwait(false);
    }

    public async Task<(long TotalRequests, long InputTokens, long OutputTokens, long CacheTokens, decimal CostUsd)> GetAdminUsageSummaryAsync(
        IReadOnlyList<string>? modelFilters = null,
        IReadOnlyList<string>? providerFilters = null,
        IReadOnlyList<string>? apiKeyFilters = null,
        IReadOnlyList<string>? nodeFilters = null,
        IReadOnlyList<string>? accountFilters = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        return await repository.GetAdminUsageSummaryAsync(modelFilters, providerFilters, apiKeyFilters, nodeFilters, accountFilters, fromUtc, toUtc, ct).ConfigureAwait(false);
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

    public Task<IReadOnlyCollection<ModelProvider>> ListModelProvidersAsync(CancellationToken ct = default)
        => repository.ListModelProvidersAsync(ct);

    public Task<ModelProvider?> GetModelProviderAsync(string id, CancellationToken ct = default)
        => repository.GetModelProviderAsync(Preconditions.RequireValue(id, nameof(id)).ToLowerInvariant(), ct);

    public async Task UpsertModelProviderAsync(ModelProvider provider, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var id = Preconditions.RequireValue(provider.Id, nameof(provider.Id));
        var displayName = Preconditions.RequireValue(provider.DisplayName, nameof(provider.DisplayName));

        if (!TagIdRegex().IsMatch(id))
            throw new ArgumentException("Provider ID must be alphanumeric with dashes, underscores, dots or slashes.", nameof(provider.Id));

        if (id.Length > 50)
            throw new ArgumentException("Provider ID cannot be longer than 50 characters.", nameof(provider.Id));

        if (displayName.Length > 200)
            throw new ArgumentException("Provider display name cannot be longer than 200 characters.", nameof(provider.DisplayName));

        if (provider.LogoBytes is { Length: > 0 })
        {
            if (provider.LogoContentType is not ("image/png" or "image/jpeg" or "image/webp" or "image/svg+xml"))
                throw new ArgumentException("Logo must be PNG, JPEG, WebP, or SVG.", nameof(provider.LogoBytes));

            if (provider.LogoBytes.Length > 256 * 1024)
                throw new ArgumentException("Logo must be at most 256 KiB.", nameof(provider.LogoBytes));
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await repository.GetModelProviderAsync(id, ct).ConfigureAwait(false);

        await repository.UpsertModelProviderAsync(provider with
        {
            Id = id,
            DisplayName = displayName,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now
        }, ct).ConfigureAwait(false);
    }

    public Task<ModelProvider?> GetModelProviderLogoAsync(string id, CancellationToken ct = default)
        => repository.GetModelProviderLogoAsync(Preconditions.RequireValue(id, nameof(id)).ToLowerInvariant(), ct);

    public Task DeleteModelProviderAsync(string id, CancellationToken ct = default)
        => repository.DeleteModelProviderAsync(Preconditions.RequireValue(id, nameof(id)).ToLowerInvariant(), ct);

    public Task<string?> GetPlatformSettingAsync(string key, CancellationToken ct = default)
        => repository.GetPlatformSettingAsync(Preconditions.RequireValue(key, nameof(key)), ct);

    public Task SetPlatformSettingAsync(string key, string value, CancellationToken ct = default)
        => repository.SetPlatformSettingAsync(Preconditions.RequireValue(key, nameof(key)), value ?? string.Empty, ct);

    public FxQuote GetFxQuote(string providerCurrency, DateTimeOffset nowUtc) =>
        RequireFx().GetQuote(Preconditions.RequireValue(providerCurrency, nameof(providerCurrency)), nowUtc);

    public async Task<Payment> CreatePaymentAsync(
        string userId,
        string provider,
        decimal providerAmount,
        string providerCurrency,
        decimal displayAmount,
        string displayCurrency,
        string clientToken,
        DateTimeOffset nowUtc,
        decimal? bonusPercent = null,
        CancellationToken ct = default)
    {
        if (providerAmount <= 0 || displayAmount <= 0)
            throw new PaymentOperationException(ErrorCodes.PaymentAmountOutOfRange);

        var quote = RequireFx().GetQuote(providerCurrency, nowUtc);
        var credited = decimal.Round(providerAmount * quote.UsdPerUnit, 6, MidpointRounding.AwayFromZero);
        var limits = paymentOptions?.Value ?? new PaymentOptions();
        if (credited < limits.MinTopUpUsd || credited > limits.MaxTopUpUsd)
            throw new PaymentOperationException(ErrorCodes.PaymentAmountOutOfRange);

        var id = Guid.NewGuid().ToString("N");
        return await repository.CreatePaymentAsync(new Payment(
            id,
            Preconditions.RequireValue(userId, nameof(userId)),
            Preconditions.RequireValue(provider, nameof(provider)),
            null,
            id,
            Preconditions.RequireValue(clientToken, nameof(clientToken)),
            displayAmount,
            Preconditions.RequireValue(displayCurrency, nameof(displayCurrency)).ToUpperInvariant(),
            providerAmount,
            Preconditions.RequireValue(providerCurrency, nameof(providerCurrency)).ToUpperInvariant(),
            MoneyAmount.Usd(credited),
            quote.Rate,
            quote.Source,
            quote.FxAtUtc,
            PaymentStatus.Created,
            null,
            null,
            null,
            nowUtc,
            nowUtc,
            null,
            null,
            null,
            null,
            bonusPercent), ct).ConfigureAwait(false);
    }

    public async Task AttachProviderRefAsync(string paymentId, string providerPaymentId, string? payUrl, DateTimeOffset? expiresAt, CancellationToken ct = default)
    {
        if (!await repository.AttachProviderRefAsync(
            Preconditions.RequireValue(paymentId, nameof(paymentId)),
            Preconditions.RequireValue(providerPaymentId, nameof(providerPaymentId)),
            payUrl, expiresAt, ct).ConfigureAwait(false))
            throw new PaymentOperationException(ErrorCodes.PaymentInvalidTransition);
    }

    public async Task<ConfirmOutcome> ConfirmPaymentByProviderAsync(PaymentEventInput evt, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var outcome = await repository.ConfirmPaymentAtomicAsync(evt, nowUtc, ct).ConfigureAwait(false);
        if (outcome.Confirmed && outcome.Payment is not null)
        {
            EmitAnalytics(
                AnalyticsEvents.PaymentCompleted,
                () => productAnalytics!.PaymentCompleted(
                    outcome.Payment.UserId,
                    new PaymentAnalytics(
                        outcome.Payment.Provider,
                        outcome.Payment.ProviderCurrency,
                        outcome.Payment.ProviderAmount,
                        outcome.Payment.Id)));
        }

        return outcome;
    }

    public Task<ApplyOutcome> ApplyPaymentEventAsync(PaymentEventInput evt, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        repository.ApplyPaymentEventAtomicAsync(evt, nowUtc, paymentOptions?.Value.AutoReverseChargeback ?? false, ct);

    public async Task<Payment> GetPaymentAsync(string id, CancellationToken ct = default) =>
        await repository.GetPaymentAsync(Preconditions.RequireValue(id, nameof(id)), ct).ConfigureAwait(false)
        ?? throw new PaymentOperationException(ErrorCodes.PaymentNotFound);

    public async Task<IReadOnlyCollection<Payment>> ListPaymentsAsync(string userId, int? pageSize = null, CancellationToken ct = default)
    {
        var limit = pageSize is > 0 ? Math.Min(pageSize.Value, paginationOptions.Value.MaxPageSize) : paginationOptions.Value.EffectivePageSize;
        return (await repository.ListPaymentsAsync(Preconditions.RequireValue(userId, nameof(userId)), limit, ct).ConfigureAwait(false)).Items;
    }

    public Task<IReadOnlyCollection<Payment>> AcquireStalePaymentsAsync(string workerId, TimeSpan lease, DateTimeOffset olderThanUtc, int limit, CancellationToken ct = default) =>
        repository.AcquireStalePaymentsAsync(Preconditions.RequireValue(workerId, nameof(workerId)), lease, olderThanUtc, limit, ct);

    private static void ValidatePositiveUsd(MoneyAmount amount, string name)
    {
        if (amount.Currency != Currency.Usd)
            throw new ArgumentException("Only $ is supported.", name);

        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(name, "Amount must be positive.");
    }

    private static void ValidateOptionalPositiveUsd(MoneyAmount? amount, string name)
    {
        if (!amount.HasValue)
            return;

        ValidatePositiveUsd(amount.Value, name);
    }

    private static DateTimeOffset GetUtcDayStart(DateTimeOffset nowUtc) =>
        new(nowUtc.ToUniversalTime().Date, TimeSpan.Zero);

    private static DateTimeOffset GetUtcWeekStart(DateTimeOffset nowUtc)
    {
        var dayStart = GetUtcDayStart(nowUtc);
        var daysSinceMonday = ((int)dayStart.DayOfWeek + 6) % 7;
        return dayStart.AddDays(-daysSinceMonday);
    }

    private FxQuoteService RequireFx() =>
        fxQuoteService ?? throw new InvalidOperationException("FxQuoteService is not configured.");

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

    private void EmitAnalytics(string eventName, Action emit)
    {
        if (productAnalytics is null)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                emit();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Analytics emission failed for event {EventName}.", eventName);
            }
        });
    }

    private void EmitApiRequestCompleted(UsageEvent usageEvent)
    {
        ApiEndpoint? endpoint = null;
        if (!string.IsNullOrEmpty(usageEvent.Endpoint))
        {
            endpoint = usageEvent.Endpoint switch
            {
                "chat_completions" => ApiEndpoint.ChatCompletions,
                "tts" => ApiEndpoint.Tts,
                _ => null,
            };
        }

        EmitAnalytics(
            AnalyticsEvents.ApiRequestCompleted,
            () => productAnalytics!.ApiRequestCompleted(
                usageEvent.UserId,
                new ApiRequestAnalytics(
                    usageEvent.ModelId,
                    MapStatusToStatusCode(usageEvent.Status),
                    (int)usageEvent.Usage.InputTokens,
                    (int)usageEvent.Usage.OutputTokens,
                    (long)(usageEvent.CompletedAtUtc - usageEvent.StartedAtUtc).TotalMilliseconds,
                    usageEvent.RequestId,
                    endpoint,
                    usageEvent.Stream)));
    }

    public async Task<GrantStartBalanceResult> GrantStartBalanceAsync(
        string userId, decimal amountUsd, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        _ = Preconditions.RequireValue(userId, nameof(userId));
        if (amountUsd <= 0 || amountUsd > 100)
            return new GrantStartBalanceResult(false, null, "invalid_amount");

        var existing = await repository.HasPromo1EntryAsync(userId, ct).ConfigureAwait(false);
        if (existing is not null)
            return new GrantStartBalanceResult(false, existing, null);

        var entry = await repository.AddLedgerEntryAsync(new LedgerEntry(
            Guid.NewGuid().ToString("N"),
            userId,
            LedgerEntryType.Promo1,
            MoneyAmount.Usd(amountUsd),
            $"promo1:{userId}",
            $"Start balance promo ${amountUsd:F2}",
            nowUtc), ct).ConfigureAwait(false);

        return new GrantStartBalanceResult(true, entry.Id, null);
    }

    public async Task<GrantFirstTopUpBonusResult> GrantFirstTopUpBonusAsync(
        string paymentId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        _ = Preconditions.RequireValue(paymentId, nameof(paymentId));

        var payment = await repository.GetPaymentAsync(paymentId, ct).ConfigureAwait(false);
        if (payment is null)
            return new GrantFirstTopUpBonusResult(false, null, "payment_not_found");

        if (payment.Status != PaymentStatus.Paid)
            return new GrantFirstTopUpBonusResult(false, null, "payment_not_paid");

        if (payment.BonusPercent is null || payment.BonusPercent <= 0)
            return new GrantFirstTopUpBonusResult(false, null, "no_bonus_snapshot");

        var existing = await repository.HasFirstTopUpBonusEntryAsync(paymentId, ct).ConfigureAwait(false);
        if (existing)
            return new GrantFirstTopUpBonusResult(false, null, null);

        var hasPaidBefore = await repository.HasUserHadPaidTopUpAsync(payment.UserId, ct).ConfigureAwait(false);
        if (hasPaidBefore)
            return new GrantFirstTopUpBonusResult(false, null, "not_first_paid_top_up");

        var bonusUsd = payment.CreditedUsd.Amount * payment.BonusPercent.Value / 100m;
        var entry = await repository.AddLedgerEntryAsync(new LedgerEntry(
            Guid.NewGuid().ToString("N"),
            payment.UserId,
            LedgerEntryType.FirstTopUpBonus,
            MoneyAmount.Usd(bonusUsd),
            $"first_top_up_bonus:{paymentId}",
            $"First top-up bonus {payment.BonusPercent}% on payment {paymentId}",
            nowUtc), ct).ConfigureAwait(false);

        await repository.UpdatePaymentBonusAsync(paymentId, payment.BonusPercent.Value, bonusUsd, ct).ConfigureAwait(false);

        return new GrantFirstTopUpBonusResult(true, entry.Id, null);
    }

    private static int MapStatusToStatusCode(string? status)
    {
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "usage_missing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return 200;
        }

        if (string.Equals(status, "invalid_token_count", StringComparison.OrdinalIgnoreCase))
        {
            return 400;
        }

        return 500;
    }
}

public sealed class PaymentOperationException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
