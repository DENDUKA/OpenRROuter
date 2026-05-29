using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Application.Services;

public interface IBillingRepository
{
    Task<LedgerEntry> AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default);

    Task<(IReadOnlyCollection<LedgerEntry> Items, bool HasMore)> ListTopUpsAsync(string userId, int limit, CancellationToken ct = default);

    Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default);

    Task UpsertModelAsync(ModelPrice model, CancellationToken ct = default);

    Task<ModelPrice?> GetEnabledModelAsync(string modelId, CancellationToken ct = default);

    Task<ModelPrice?> GetModelAsync(string modelId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListEnabledModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default);

    Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListAllModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default);

    Task<UsageRecord> AddUsageAsync(UsageRecord record, CancellationToken ct = default);

    Task<UsageRecord?> GetUsageAsync(string requestId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<UsageRecord> Items, bool HasMore)> ListUsageAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? userId = null, CancellationToken ct = default);

    Task<ActivityByTokenRecord> AddActivityByTokenAsync(ActivityByTokenRecord record, CancellationToken ct = default);

    Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        DateTimeOffset? afterCreatedAtUtc = null,
        CancellationToken ct = default);

    Task<ActivitySummary> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default);

    Task AddAnomalyAsync(BillingAnomaly anomaly, CancellationToken ct = default);

    Task<(IReadOnlyCollection<BillingAnomaly> Items, bool HasMore)> ListAnomaliesAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, CancellationToken ct = default);

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
}
