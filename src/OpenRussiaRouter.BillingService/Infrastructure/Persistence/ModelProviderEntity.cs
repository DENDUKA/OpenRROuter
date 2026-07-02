namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class ModelProviderEntity
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public string? DescriptionRu { get; set; }
    public string? DescriptionEn { get; set; }
    public byte[]? LogoBytes { get; set; }
    public string? LogoContentType { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
