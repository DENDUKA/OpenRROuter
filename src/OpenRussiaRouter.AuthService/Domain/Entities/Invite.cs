using OpenRussiaRouter.AuthService.Domain.Enums;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

/// <summary>
/// Admin-created registration invite.
/// </summary>
public sealed class Invite
{
    /// <summary>
    /// Creates an invite.
    /// </summary>
    /// <param name="id">Invite id.</param>
    /// <param name="email">Invited email.</param>
    /// <param name="role">Role assigned to the created user.</param>
    /// <param name="tokenHash">Hash of the one-time invite token.</param>
    /// <param name="expiresAtUtc">Expiration timestamp.</param>
    /// <param name="createdByAdminId">Admin user id that created the invite.</param>
    /// <param name="createdAtUtc">Creation timestamp.</param>
    public Invite(
        string id,
        string email,
        UserRole role,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        string createdByAdminId,
        DateTimeOffset createdAtUtc)
    {
        Id = RequireValue(id, nameof(id));
        Email = NormalizeEmail(email);
        Role = role;
        TokenHash = RequireValue(tokenHash, nameof(tokenHash));
        ExpiresAtUtc = expiresAtUtc;
        CreatedByAdminId = RequireValue(createdByAdminId, nameof(createdByAdminId));
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Invite id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Invited email.
    /// </summary>
    public string Email { get; }

    /// <summary>
    /// Role assigned to the created user.
    /// </summary>
    public UserRole Role { get; }

    /// <summary>
    /// Hash of the raw invite token.
    /// </summary>
    public string TokenHash { get; }

    /// <summary>
    /// Expiration timestamp.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    /// <summary>
    /// Admin user id that created the invite.
    /// </summary>
    public string CreatedByAdminId { get; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Timestamp when the invite was used.
    /// </summary>
    public DateTimeOffset? UsedAtUtc { get; private set; }

    /// <summary>
    /// True when the invite has already been used.
    /// </summary>
    public bool IsUsed => UsedAtUtc.HasValue;

    /// <summary>
    /// Marks the invite as used.
    /// </summary>
    /// <param name="usedAtUtc">Use timestamp.</param>
    public void MarkUsed(DateTimeOffset usedAtUtc)
    {
        if (IsUsed)
        {
            throw new InvalidOperationException("Invite has already been used.");
        }

        UsedAtUtc = usedAtUtc;
    }

    /// <summary>
    /// Returns true when the invite is expired at the given time.
    /// </summary>
    /// <param name="nowUtc">Current UTC timestamp.</param>
    /// <returns>Whether invite is expired.</returns>
    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    private static string NormalizeEmail(string email)
    {
        var value = RequireValue(email, nameof(email)).Trim().ToLowerInvariant();
        if (!value.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Email must contain '@'.", nameof(email));
        }

        return value;
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", name);
        }

        return value;
    }
}

