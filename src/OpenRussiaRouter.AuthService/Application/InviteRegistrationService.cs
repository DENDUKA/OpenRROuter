using OpenRussiaRouter.AuthService.Domain;
using OpenRussiaRouter.AuthService.Security;

namespace OpenRussiaRouter.AuthService.Application;

/// <summary>
/// Handles invite creation and one-time invite completion.
/// </summary>
public sealed class InviteRegistrationService(Sha256SecretHasher secretHasher, Pbkdf2PasswordHasher passwordHasher)
{
    private const string InvitePrefix = "orr_invite";

    /// <summary>
    /// Creates an invite and returns the raw one-time token.
    /// </summary>
    /// <param name="email">Invited email.</param>
    /// <param name="role">Role assigned to the created user.</param>
    /// <param name="createdByAdminId">Admin user id.</param>
    /// <param name="expiresAtUtc">Expiration timestamp.</param>
    /// <param name="nowUtc">Creation timestamp.</param>
    /// <returns>Invite plus raw token shown once.</returns>
    public CreatedInvite CreateInvite(
        string email,
        UserRole role,
        string createdByAdminId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset nowUtc)
    {
        if (expiresAtUtc <= nowUtc)
        {
            throw new ArgumentException("Invite expiration must be in the future.", nameof(expiresAtUtc));
        }

        var rawToken = SecretGenerator.Generate(InvitePrefix);
        var invite = new Invite(
            Guid.NewGuid().ToString("N"),
            email,
            role,
            secretHasher.Hash(rawToken),
            expiresAtUtc,
            createdByAdminId,
            nowUtc);

        return new CreatedInvite(invite, rawToken);
    }

    /// <summary>
    /// Completes an invite and returns the created user.
    /// </summary>
    /// <param name="invite">Stored invite.</param>
    /// <param name="rawToken">Raw invite token.</param>
    /// <param name="password">Raw password.</param>
    /// <param name="nowUtc">Current timestamp.</param>
    /// <returns>Created user account.</returns>
    public UserAccount CompleteInvite(Invite invite, string rawToken, string password, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(invite);

        if (invite.IsUsed)
        {
            throw new InvalidOperationException("Invite has already been used.");
        }

        if (invite.IsExpired(nowUtc))
        {
            throw new InvalidOperationException("Invite has expired.");
        }

        if (!secretHasher.Verify(rawToken, invite.TokenHash))
        {
            throw new InvalidOperationException("Invite token is invalid.");
        }

        var user = new UserAccount(
            Guid.NewGuid().ToString("N"),
            invite.Email,
            invite.Role,
            passwordHasher.HashPassword(password),
            nowUtc);

        invite.MarkUsed(nowUtc);
        return user;
    }
}

/// <summary>
/// Invite creation result containing the stored invite and raw token shown once.
/// </summary>
/// <param name="Invite">Stored invite.</param>
/// <param name="RawToken">Raw one-time token.</param>
public sealed record CreatedInvite(Invite Invite, string RawToken);

