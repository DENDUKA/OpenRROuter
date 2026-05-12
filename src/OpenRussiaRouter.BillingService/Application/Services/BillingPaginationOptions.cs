namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class BillingPaginationOptions
{

    public int DefaultPageSize { get; set; } = 1000;

    public int MaxPageSize { get; set; } = 5000;
}
