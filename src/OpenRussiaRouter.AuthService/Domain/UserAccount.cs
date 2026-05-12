namespace OpenRussiaRouter.AuthService.Domain;

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
        Id = RequireValue(id, nameof(id));
        Email = NormalizeEmail(email);
        Role = role;
        PasswordHash = RequireValue(passwordHash, nameof(passwordHash));
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

