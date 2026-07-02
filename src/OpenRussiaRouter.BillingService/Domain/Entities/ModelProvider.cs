namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record ModelProvider(
    string Id,
    string DisplayName,
    string? DescriptionRu = null,
    string? DescriptionEn = null,
    byte[]? LogoBytes = null,
    string? LogoContentType = null,
    DateTimeOffset CreatedAtUtc = default,
    DateTimeOffset UpdatedAtUtc = default);
