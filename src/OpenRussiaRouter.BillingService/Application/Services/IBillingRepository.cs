using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Application.Services;

public interface IBillingRepository
{

    LedgerEntry AddLedgerEntry(LedgerEntry entry);

    (IReadOnlyCollection<LedgerEntry> Items, bool HasMore) ListTopUps(string userId, int limit);

    MoneyAmount GetBalance(string userId);

    void UpsertModel(ModelPrice model);

    ModelPrice? GetEnabledModel(string modelId);

    (IReadOnlyCollection<ModelPrice> Items, bool HasMore) ListEnabledModels(int limit, string? afterModelId = null);

    (IReadOnlyCollection<ModelPrice> Items, bool HasMore) ListAllModels(int limit, string? afterModelId = null);

    UsageRecord AddUsage(UsageRecord record);

    UsageRecord? GetUsage(string requestId);

    (IReadOnlyCollection<UsageRecord> Items, bool HasMore) ListUsage(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? userId = null);

    ActivityByTokenRecord AddActivityByToken(ActivityByTokenRecord record);

    (IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore) ListActivityByToken(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? afterCreatedAtUtc = null);

    void AddAnomaly(BillingAnomaly anomaly);

    (IReadOnlyCollection<BillingAnomaly> Items, bool HasMore) ListAnomalies(int limit, DateTimeOffset? afterCreatedAtUtc = null);
}
