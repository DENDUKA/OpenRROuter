using OpenRussiaRouter.AuthService.Application.Abstractions;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class InviteRegistrationService(ISecretHasher secretHasher, IPasswordHasher passwordHasher, ISecretGenerator secretGenerator)
{
    private const string InvitePrefix = "rwt_invite";

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

        var rawToken = secretGenerator.Generate(InvitePrefix);
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

public sealed record CreatedInvite(Invite Invite, string RawToken);

