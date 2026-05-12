using OpenRussiaRouter.AuthService.Domain.Enums;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

/// <summary>
/// Authenticated UI session issued after successful email/password login.
/// </summary>
/// <param name="Token">Raw session token returned to the caller.</param>
/// <param name="UserId">Authenticated user id.</param>
/// <param name="Role">Authenticated user role.</param>
/// <param name="IssuedAtUtc">Issue timestamp.</param>
/// <param name="ExpiresAtUtc">Expiration timestamp.</param>
public sealed record UserSession(
    string Token,
    string UserId,
    UserRole Role,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

