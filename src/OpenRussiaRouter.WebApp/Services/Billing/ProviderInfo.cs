namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record ProviderInfo(
    string Id,
    string DisplayName,
    string? DescriptionRu = null,
    string? DescriptionEn = null,
    byte[]? LogoBytes = null,
    string? LogoContentType = null,
    DateTimeOffset CreatedAtUtc = default,
    DateTimeOffset UpdatedAtUtc = default);
