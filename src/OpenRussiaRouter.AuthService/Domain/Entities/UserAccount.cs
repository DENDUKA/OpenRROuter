using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

public sealed class UserAccount
{

    public UserAccount(string id, string email, UserRole role, string? passwordHash, DateTimeOffset createdAtUtc)
    {
        Id = Preconditions.RequireValue(id, nameof(id));
        Email = EmailValidator.Normalize(email);
        Role = role;
        PasswordHash = passwordHash;
        CreatedAtUtc = createdAtUtc;
    }

    public string Id { get; }

    public string Email { get; }

    public UserRole Role { get; }

    public UserStatus Status { get; private set; } = UserStatus.Active;

    public string? DisplayName { get; private set; }

    public int MaxApiKeys { get; private set; } = 10;

    public string? PasswordHash { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public void Disable() => Status = UserStatus.Disabled;

    public void Activate() => Status = UserStatus.Active;

    public void UpdateDetails(string? displayName, int maxApiKeys)
    {
        if (maxApiKeys < 0) throw new ArgumentOutOfRangeException(nameof(maxApiKeys));
        DisplayName = displayName;
        MaxApiKeys = maxApiKeys;
    }

    public static UserAccount CreateForExternalLogin(string id, string email, UserRole role, DateTimeOffset createdAtUtc)
        => new(id, email, role, null, createdAtUtc) { MaxApiKeys = 10 };

    /// <summary>
    /// A self-serve email+password signup. The account starts in
    /// <see cref="UserStatus.PendingVerification"/> and cannot log in until the
    /// emailed code is confirmed (see <see cref="Activate"/>).
    /// </summary>
    public static UserAccount CreateForEmailSignup(string id, string email, UserRole role, string passwordHash, DateTimeOffset createdAtUtc)
    {
        var user = new UserAccount(id, email, role, passwordHash, createdAtUtc);
        user.Status = UserStatus.PendingVerification;
        user.MaxApiKeys = 10;
        return user;
    }

    /// <summary>Reconstructs a persisted account, preserving its stored status.</summary>
    public static UserAccount Restore(string id, string email, UserRole role, string? passwordHash, UserStatus status, DateTimeOffset createdAtUtc, string? displayName = null, int maxApiKeys = 10)
    {
        var user = new UserAccount(id, email, role, passwordHash, createdAtUtc);
        user.Status = status;
        user.DisplayName = displayName;
        user.MaxApiKeys = maxApiKeys;
        return user;
    }

}

