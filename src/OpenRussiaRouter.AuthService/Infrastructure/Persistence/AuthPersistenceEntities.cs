using OpenRussiaRouter.AuthService.Domain;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

/// <summary>
/// Persisted user account row.
/// </summary>
public sealed class UserAccountEntity
{
    /// <summary>
    /// Stable user id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Normalized user email.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// User role.
    /// </summary>
    public UserRole Role { get; set; }

    /// <summary>
    /// Current user status.
    /// </summary>
    public UserStatus Status { get; set; }

    /// <summary>
    /// Stored password hash.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Persisted invite row.
/// </summary>
public sealed class InviteEntity
{
    /// <summary>
    /// Invite id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Invited email.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Role assigned to the created user.
    /// </summary>
    public UserRole Role { get; set; }

    /// <summary>
    /// Hash of the one-time invite token.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Expiration timestamp.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// Admin user id that created the invite.
    /// </summary>
    public string CreatedByAdminId { get; set; } = string.Empty;

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Timestamp when the invite was used.
    /// </summary>
    public DateTimeOffset? UsedAtUtc { get; set; }
}

/// <summary>
/// Persisted API key row.
/// </summary>
public sealed class ApiKeyEntity
{
    /// <summary>
    /// API key id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Owner user id.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// User-visible key name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Safe visible key prefix.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the raw API key.
    /// </summary>
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Last successful validation timestamp.
    /// </summary>
    public DateTimeOffset? LastUsedAtUtc { get; set; }

    /// <summary>
    /// Revocation timestamp.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
