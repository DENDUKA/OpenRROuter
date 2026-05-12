namespace OpenRussiaRouter.BillingService.Application.Services;

/// <summary>
/// Configuration options for BillingApplicationService pagination limits.
/// </summary>
public sealed class BillingPaginationOptions
{
    /// <summary>
    /// Default maximum number of items to return in list queries.
    /// </summary>
    public int DefaultPageSize { get; set; } = 1000;

    /// <summary>
    /// Maximum allowed page size to prevent excessive queries.
    /// </summary>
    public int MaxPageSize { get; set; } = 5000;
}
