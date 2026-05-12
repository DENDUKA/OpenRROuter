using OpenRussiaRouter.AuthService.Domain.Enums;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

public sealed record UserSession(
    string Token,
    string UserId,
    UserRole Role,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

