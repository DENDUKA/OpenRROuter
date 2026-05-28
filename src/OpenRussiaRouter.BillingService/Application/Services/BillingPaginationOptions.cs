using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class BillingPaginationOptions
{
    [Range(1, 10000)]
    public int DefaultPageSize { get; set; } = 1000;

    [Range(1, 10000)]
    public int MaxPageSize { get; set; } = 5000;

    public int EffectivePageSize => Math.Min(DefaultPageSize, MaxPageSize);
}
