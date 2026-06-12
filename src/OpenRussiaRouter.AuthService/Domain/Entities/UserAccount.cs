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

    public string DisplayCurrency { get; private set; } = "RUB";

    public string? PasswordHash { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? LockedUntilUtc { get; private set; }

    public void Disable() => Status = UserStatus.Disabled;

    public void Activate() => Status = UserStatus.Active;

    public bool IsLockedOut(DateTimeOffset nowUtc) => LockedUntilUtc is { } until && nowUtc < until;

    /// <summary>
    /// Counts a failed password attempt; once <paramref name="maxAttempts"/> is reached the
    /// account locks for <paramref name="lockoutDuration"/> and the counter restarts, so the
    /// user gets a fresh set of attempts after the lock expires. Returns true when this
    /// attempt triggered the lock.
    /// </summary>
    public bool RegisterFailedLogin(DateTimeOffset nowUtc, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginCount++;
        if (FailedLoginCount < maxAttempts)
        {
            return false;
        }

        LockedUntilUtc = nowUtc.Add(lockoutDuration);
        FailedLoginCount = 0;
        return true;
    }

    public void ResetLoginThrottle()
    {
        FailedLoginCount = 0;
        LockedUntilUtc = null;
    }

    public void UpdateDetails(string? displayName, int maxApiKeys, string? displayCurrency = null)
    {
        if (maxApiKeys < 0) throw new ArgumentOutOfRangeException(nameof(maxApiKeys));
        DisplayName = displayName;
        MaxApiKeys = maxApiKeys;
        if (displayCurrency != null) DisplayCurrency = displayCurrency;
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
    public static UserAccount Restore(string id, string email, UserRole role, string? passwordHash, UserStatus status, DateTimeOffset createdAtUtc, string? displayName = null, int maxApiKeys = 10, string displayCurrency = "RUB", int failedLoginCount = 0, DateTimeOffset? lockedUntilUtc = null)
    {
        var user = new UserAccount(id, email, role, passwordHash, createdAtUtc);
        user.Status = status;
        user.DisplayName = displayName;
        user.MaxApiKeys = maxApiKeys;
        user.DisplayCurrency = displayCurrency;
        user.FailedLoginCount = failedLoginCount;
        user.LockedUntilUtc = lockedUntilUtc;
        return user;
    }

}

