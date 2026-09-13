using System.Globalization;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingTokenUsage = OpenRussiaRouter.Contracts.Grpc.Billing.V1.TokenUsage;

namespace OpenRussiaRouter.WebApp.Services.Billing;

/// <summary>
/// Facade over domain-specific billing sub-providers. Preserves the original public API
/// so existing consumers and test fakes continue to work unchanged (T-061).
/// </summary>
public class BillingProvider(BillingGrpc.BillingServiceClient client)
{
    private readonly BillingCatalogProvider _catalog = new(client);
    private readonly BillingUsageProvider _usage = new(client);
    private readonly BillingPaymentsProvider _payments = new(client);

    // ── Balance ──────────────────────────────────────────────────────────────

    public Task<BalanceInfo> GetBalanceAsync(string userId, CancellationToken cancellationToken = default)
        => _payments.GetBalanceAsync(userId, cancellationToken);

    // ── Usage / Activity ─────────────────────────────────────────────────────

    public Task<(IReadOnlyList<UsageInfo> Items, bool HasMore, string? NextCursor)> ListUsageAsync(string userId, string? afterCursor = null, CancellationToken cancellationToken = default)
        => _usage.ListUsageAsync(userId, afterCursor, cancellationToken);

    public Task<string?> RecordUsageAsync(
        string requestId, string userId, string apiKeyId, string apiKeyPrefix,
        string modelId, string routerNodeId,
        long inputTokens, long outputTokens, long cacheTokens,
        string status, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
        => _usage.RecordUsageAsync(requestId, userId, apiKeyId, apiKeyPrefix, modelId, routerNodeId,
            inputTokens, outputTokens, cacheTokens, status, startedAtUtc, completedAtUtc, cancellationToken);

    public virtual Task<(IReadOnlyList<ActivityByTokenInfo> Items, bool HasMore, string? NextCursor)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null,
        string? modelIdFilter = null, string? apiKeyIdFilter = null, string? afterCursor = null,
        int pageSize = 0, CancellationToken cancellationToken = default)
        => _usage.ListActivityByTokenAsync(apiKeyIds, fromUtc, toUtc, modelIdFilter, apiKeyIdFilter, afterCursor, pageSize, cancellationToken);

    public virtual Task<ActivitySummaryInfo> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
        => _usage.GetActivitySummaryAsync(apiKeyIds, fromUtc, toUtc, cancellationToken);

    // ── API Key Limits ───────────────────────────────────────────────────────

    public Task<ApiKeyLimitSettingsInfo> GetApiKeyLimitSettingsAsync(string userId, string apiKeyId, CancellationToken cancellationToken = default)
        => _payments.GetApiKeyLimitSettingsAsync(userId, apiKeyId, cancellationToken);

    public Task UpsertApiKeyLimitSettingsAsync(string userId, string apiKeyId, decimal? dailyLimitUsd, decimal? weeklyLimitUsd, CancellationToken cancellationToken = default)
        => _payments.UpsertApiKeyLimitSettingsAsync(userId, apiKeyId, dailyLimitUsd, weeklyLimitUsd, cancellationToken);

    public Task<IReadOnlyDictionary<string, ApiKeyLimitSummaryInfo>> ListApiKeyLimitSummariesAsync(string userId, IReadOnlyCollection<string> apiKeyIds, CancellationToken cancellationToken = default)
        => _payments.ListApiKeyLimitSummariesAsync(userId, apiKeyIds, cancellationToken);

    // ── Top-ups ──────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<TopUpInfo>> ListTopUpsAsync(string userId, CancellationToken cancellationToken = default)
        => _payments.ListTopUpsAsync(userId, cancellationToken);

    public Task<(IReadOnlyList<AdminTopUpInfo> Items, int TotalCount)> ListAllTopUpsAsync(
        int pageNumber = 0, int pageSize = 50, IReadOnlyList<string>? userIds = null,
        decimal? minAmountUsd = null, decimal? maxAmountUsd = null, CancellationToken cancellationToken = default)
        => _payments.ListAllTopUpsAsync(pageNumber, pageSize, userIds, minAmountUsd, maxAmountUsd, cancellationToken);

    public Task CreateVirtualTopUpAsync(string adminId, string userId, decimal amountUsd, string reason, CancellationToken cancellationToken = default)
        => _payments.CreateVirtualTopUpAsync(adminId, userId, amountUsd, reason, cancellationToken);

    // ── Catalog: Models ──────────────────────────────────────────────────────

    public virtual Task<IReadOnlyList<ModelInfo>> ListPublicModelsAsync(CancellationToken cancellationToken = default)
        => _catalog.ListPublicModelsAsync(cancellationToken);

    public virtual Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        => _catalog.ListModelsAsync(cancellationToken);

    public virtual Task<ModelInfo?> GetModelAsync(string modelId, CancellationToken cancellationToken = default)
        => _catalog.GetModelAsync(modelId, cancellationToken);

    public virtual Task UpsertModelAsync(ModelInfo model, CancellationToken cancellationToken = default)
        => _catalog.UpsertModelAsync(model, cancellationToken);

    public virtual Task<(int UpsertedCount, IReadOnlyList<string> FailedModelIds)> UpsertModelsAsync(IReadOnlyList<ModelInfo> models, CancellationToken cancellationToken = default)
        => _catalog.UpsertModelsAsync(models, cancellationToken);

    // ── Catalog: Providers ───────────────────────────────────────────────────

    public virtual Task<IReadOnlyList<ProviderInfo>> ListModelProvidersAsync(CancellationToken cancellationToken = default)
        => _catalog.ListModelProvidersAsync(cancellationToken);

    public virtual Task<ProviderInfo?> GetModelProviderAsync(string providerId, CancellationToken cancellationToken = default)
        => _catalog.GetModelProviderAsync(providerId, cancellationToken);

    public virtual Task UpsertModelProviderAsync(ProviderInfo provider, CancellationToken cancellationToken = default)
        => _catalog.UpsertModelProviderAsync(provider, cancellationToken);

    public virtual Task DeleteModelProviderAsync(string providerId, CancellationToken cancellationToken = default)
        => _catalog.DeleteModelProviderAsync(providerId, cancellationToken);

    // ── Catalog: Tags ────────────────────────────────────────────────────────

    public virtual Task<IReadOnlyList<TagInfo>> ListTagsAsync(CancellationToken cancellationToken = default)
        => _catalog.ListTagsAsync(cancellationToken);

    public virtual Task UpsertTagAsync(TagInfo tag, CancellationToken cancellationToken = default)
        => _catalog.UpsertTagAsync(tag, cancellationToken);

    public virtual Task DeleteTagAsync(string id, CancellationToken cancellationToken = default)
        => _catalog.DeleteTagAsync(id, cancellationToken);

    // ── Platform Settings ────────────────────────────────────────────────────

    public virtual Task<string?> GetPlatformSettingAsync(string key, CancellationToken cancellationToken = default)
        => _payments.GetPlatformSettingAsync(key, cancellationToken);

    public virtual Task SetPlatformSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        => _payments.SetPlatformSettingAsync(key, value, cancellationToken);

    // ── Payments ─────────────────────────────────────────────────────────────

    public virtual Task<(decimal Rate, string FxSource, DateTimeOffset FxAt, string? QuoteId)> GetFxQuoteAsync(string providerCurrency, CancellationToken cancellationToken = default)
        => _payments.GetFxQuoteAsync(providerCurrency, cancellationToken);

    public virtual Task<CreatedPaymentInfo> CreatePaymentAsync(
        string userId, string provider, decimal providerAmount, string providerCurrency,
        decimal displayAmount, string displayCurrency, string clientToken,
        decimal? bonusPercent = null, CancellationToken cancellationToken = default)
        => _payments.CreatePaymentAsync(userId, provider, providerAmount, providerCurrency,
            displayAmount, displayCurrency, clientToken, bonusPercent, cancellationToken);

    public virtual Task AttachProviderRefAsync(string paymentId, string providerPaymentId, string payUrl, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken = default)
        => _payments.AttachProviderRefAsync(paymentId, providerPaymentId, payUrl, expiresAtUtc, cancellationToken);

    public virtual Task<ConfirmPaymentResult> ConfirmPaymentByProviderAsync(
        string provider, string? providerPaymentId, string? orderId,
        decimal confirmedAmount, string confirmedCurrency, string? providerEventId,
        string rawPayload, bool signatureOk, CancellationToken cancellationToken = default)
        => _payments.ConfirmPaymentByProviderAsync(provider, providerPaymentId, orderId,
            confirmedAmount, confirmedCurrency, providerEventId, rawPayload, signatureOk, cancellationToken);

    public virtual Task<ApplyPaymentEventResult> ApplyPaymentEventAsync(
        string provider, string? providerPaymentId, string? orderId,
        string eventType, string? reason, decimal? confirmedAmount, string? confirmedCurrency,
        string? providerEventId, string rawPayload, bool signatureOk, CancellationToken cancellationToken = default)
        => _payments.ApplyPaymentEventAsync(provider, providerPaymentId, orderId,
            eventType, reason, confirmedAmount, confirmedCurrency, providerEventId, rawPayload, signatureOk, cancellationToken);

    public virtual Task<PaymentInfo?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
        => _payments.GetPaymentAsync(paymentId, cancellationToken);

    public virtual Task<IReadOnlyList<PaymentInfo>> AcquireStalePaymentsAsync(
        string workerId, int leaseSeconds, DateTimeOffset olderThanUtc, int limit, CancellationToken cancellationToken = default)
        => _payments.AcquireStalePaymentsAsync(workerId, leaseSeconds, olderThanUtc, limit, cancellationToken);

    // ── Promotions ───────────────────────────────────────────────────────────

    public virtual Task<bool> HasUserHadPaidTopUpAsync(string userId, CancellationToken cancellationToken = default)
        => _payments.HasUserHadPaidTopUpAsync(userId, cancellationToken);

    public virtual Task<(bool StartBalanceEnabled, decimal StartBalanceAmountUsd, bool FirstTopUpEnabled, decimal FirstTopUpPercent)> GetPromotionSettingsAsync(CancellationToken cancellationToken = default)
        => _payments.GetPromotionSettingsAsync(cancellationToken);

    public virtual Task GrantStartBalanceAsync(string userId, decimal amountUsd, CancellationToken cancellationToken = default)
        => _payments.GrantStartBalanceAsync(userId, amountUsd, cancellationToken);

    public virtual Task<OpenRussiaRouter.Contracts.Billing.GrantFirstTopUpBonusResult> GrantFirstTopUpBonusAsync(string paymentId, CancellationToken cancellationToken = default)
        => _payments.GrantFirstTopUpBonusAsync(paymentId, cancellationToken);

    // ── Anomalies / Admin Requests ───────────────────────────────────────────

    public Task<IReadOnlyList<AnomalyInfo>> ListAnomaliesAsync(CancellationToken cancellationToken = default)
        => _usage.ListAnomaliesAsync(cancellationToken);

    public Task<(IReadOnlyList<AdminRequestInfo> Items, bool HasMore, string? NextCursor)> ListAdminRequestsAsync(
        string? afterCursor = null, int pageSize = 50,
        IReadOnlyList<string>? modelFilters = null, IReadOnlyList<string>? providerFilters = null,
        IReadOnlyList<string>? apiKeyFilters = null, IReadOnlyList<string>? nodeFilters = null,
        IReadOnlyList<string>? accountFilters = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
        => _usage.ListAdminRequestsAsync(afterCursor, pageSize, modelFilters, providerFilters,
            apiKeyFilters, nodeFilters, accountFilters, fromUtc, toUtc, cancellationToken);

    public Task<(long TotalRequests, long InputTokens, long OutputTokens, long CacheTokens, decimal CostUsd)> GetAdminRequestsSummaryAsync(
        IReadOnlyList<string>? modelFilters = null, IReadOnlyList<string>? providerFilters = null,
        IReadOnlyList<string>? apiKeyFilters = null, IReadOnlyList<string>? nodeFilters = null,
        IReadOnlyList<string>? accountFilters = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
        => _usage.GetAdminRequestsSummaryAsync(modelFilters, providerFilters, apiKeyFilters,
            nodeFilters, accountFilters, fromUtc, toUtc, cancellationToken);

    public virtual Task<(int TopupsCount, decimal TotalRub, decimal TotalUsd, int ActiveApiUsers)> GetDailyStatsAsync(
        DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
        => _payments.GetDailyStatsAsync(sinceUtc, cancellationToken);
}
