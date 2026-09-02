using OpenRussiaRouter.BillingService.Domain.Enums;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record YandexMetrikaOfflineConversion(
    string Id,
    string DedupKey,
    string UserId,
    string Target,
    DateTimeOffset OccurredAtUtc,
    decimal? Price,
    string? Currency,
    string? ResolvedClientId,
    YandexMetrikaOfflineConversionStatus Status,
    string? LeaseOwner,
    DateTimeOffset? LeaseUntilUtc,
    int Attempts,
    DateTimeOffset NextAttemptAtUtc,
    long? UploadId,
    string? UploadStatus,
    string Comment,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
