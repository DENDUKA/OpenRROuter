using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

/// <summary>
/// Auth user account created from an admin invite.
/// </summary>
public sealed class UserAccount
{
    /// <summary>
    /// Creates a user account.
    /// </summary>
    /// <param name="id">Stable user id.</param>
    /// <param name="email">Normalized user email.</param>
    /// <param name="role">User role.</param>
    /// <param name="passwordHash">Stored password hash.</param>
    /// <param name="createdAtUtc">Creation timestamp.</param>
    public UserAccount(string id, string email, UserRole role, string passwordHash, DateTimeOffset createdAtUtc)
    {
        Id = Preconditions.RequireValue(id, nameof(id));
        Email = EmailValidator.Normalize(email);
        Role = role;
        PasswordHash = Preconditions.RequireValue(passwordHash, nameof(passwordHash));
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Stable user id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Normalized user email.
    /// </summary>
    public string Email { get; }

    /// <summary>
    /// User role.
    /// </summary>
    public UserRole Role { get; }

    /// <summary>
    /// Current user status.
    /// </summary>
    public UserStatus Status { get; private set; } = UserStatus.Active;

    /// <summary>
    /// Stored password hash.
    /// </summary>
    public string PasswordHash { get; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Disables the user account.
    /// </summary>
    public void Disable() => Status = UserStatus.Disabled;

}

