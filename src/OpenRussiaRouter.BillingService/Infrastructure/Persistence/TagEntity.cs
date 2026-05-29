namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class TagEntity
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Color { get; set; }
}
