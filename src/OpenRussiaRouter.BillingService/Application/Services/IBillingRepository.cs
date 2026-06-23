using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Application.Services;

public interface IBillingRepository
{
    Task<LedgerEntry> AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default);

    Task<(IReadOnlyCollection<LedgerEntry> Items, bool HasMore)> ListTopUpsAsync(string userId, int limit, CancellationToken ct = default);

    Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default);

    Task<ApiKeySpendLimit?> GetApiKeySpendLimitAsync(string userId, string apiKeyId, CancellationToken ct = default);

    Task UpsertApiKeySpendLimitAsync(ApiKeySpendLimit limit, CancellationToken ct = default);

    Task ClearApiKeySpendLimitAsync(string userId, string apiKeyId, CancellationToken ct = default);

    Task<IReadOnlyCollection<ApiKeyLimitSummary>> ListApiKeyLimitSummariesAsync(
        string userId,
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset dayStartUtc,
        DateTimeOffset weekStartUtc,
        CancellationToken ct = default);

    Task UpsertModelAsync(ModelPrice model, CancellationToken ct = default);

    Task<ModelPrice?> GetEnabledModelAsync(string modelId, CancellationToken ct = default);

    Task<ModelPrice?> GetModelAsync(string modelId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListEnabledModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default);

    Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListAllModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default);

    Task<UsageRecord?> GetUsageAsync(string requestId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<UsageRecord> Items, bool HasMore)> ListUsageAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? afterRequestId = null, string? userId = null, CancellationToken ct = default);

    Task RecordUsageWithTransactionAsync(ActivityByTokenRecord activity, UsageRecord? usage, LedgerEntry? ledgerEntry, CancellationToken ct = default);

    Task<ActivityByTokenRecord> AddActivityByTokenAsync(ActivityByTokenRecord record, CancellationToken ct = default);

    Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore, int TotalCount)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        DateTimeOffset? afterCreatedAtUtc = null,
        string? afterRequestId = null,
        int offset = 0,
        CancellationToken ct = default);

    Task<ActivitySummary> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default);

    Task AddAnomalyAsync(BillingAnomaly anomaly, CancellationToken ct = default);

    Task<(IReadOnlyCollection<BillingAnomaly> Items, bool HasMore)> ListAnomaliesAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? afterId = null, CancellationToken ct = default);

    Task<(IReadOnlyCollection<AdminRequestRecord> Items, bool HasMore)> GetAdminUsageRecordsAsync(
        int limit,
        string? afterCursor = null,
        string? modelFilter = null,
        string? providerFilter = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default);

    Task<IReadOnlyCollection<Tag>> ListTagsAsync(CancellationToken ct = default);
    Task UpsertTagAsync(Tag tag, CancellationToken ct = default);
    Task DeleteTagAsync(string id, CancellationToken ct = default);

    Task<string?> GetPlatformSettingAsync(string key, CancellationToken ct = default);
    Task SetPlatformSettingAsync(string key, string value, CancellationToken ct = default);

    Task<Payment> CreatePaymentAsync(Payment payment, CancellationToken ct = default);
    Task<bool> AttachProviderRefAsync(string paymentId, string providerPaymentId, string? payUrl, DateTimeOffset? expiresAt, CancellationToken ct = default);
    Task<Payment?> GetPaymentAsync(string id, CancellationToken ct = default);
    Task<(IReadOnlyCollection<Payment> Items, bool HasMore)> ListPaymentsAsync(string userId, int limit, CancellationToken ct = default);
    Task<ConfirmOutcome> ConfirmPaymentAtomicAsync(PaymentEventInput evt, DateTimeOffset nowUtc, CancellationToken ct = default);
    Task<ApplyOutcome> ApplyPaymentEventAtomicAsync(PaymentEventInput evt, DateTimeOffset nowUtc, bool autoReverseChargeback = false, CancellationToken ct = default);
    Task<IReadOnlyCollection<PaymentEvent>> ListUnprocessedEventsAsync(int limit, CancellationToken ct = default);
    Task<IReadOnlyCollection<Payment>> AcquireStalePaymentsAsync(string workerId, TimeSpan lease, DateTimeOffset olderThanUtc, int limit, CancellationToken ct = default);
    Task<int> AcquireUnprocessedEventsAsync(int limit, DateTimeOffset nowUtc, bool autoReverseChargeback = false, CancellationToken ct = default);
}
