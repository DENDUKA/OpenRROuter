namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

internal sealed class YandexMetrikaOfflineConversionEntity
{
    public string Id { get; set; } = string.Empty;
    public string DedupKey { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? ResolvedClientId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public long? UploadId { get; set; }
    public string? UploadStatus { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
