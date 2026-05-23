namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

internal sealed class ActivityModelSummaryEntity
{
    public string ModelId { get; set; } = string.Empty;
    public long Requests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheTokens { get; set; }
    public decimal CostRub { get; set; }
}

internal sealed class ActivityKeySummaryEntity
{
    public string ApiKeyId { get; set; } = string.Empty;
    public string ApiKeyPrefix { get; set; } = string.Empty;
    public long Requests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheTokens { get; set; }
    public decimal CostRub { get; set; }
}
