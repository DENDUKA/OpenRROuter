namespace OpenRussiaRouter.AuthService.Domain.Entities;

public sealed record ExternalLogin(
    string Id,
    string UserId,
    string Provider,
    string ProviderSub,
    DateTimeOffset CreatedAtUtc);
