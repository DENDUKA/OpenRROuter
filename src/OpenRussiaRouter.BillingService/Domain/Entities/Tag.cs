namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record Tag(
    string Id,
    string DisplayName,
    string? Color);
