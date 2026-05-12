namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class BillingAnomalyEntity
{
    public string Id { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}