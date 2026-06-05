using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public static class AuthPersistenceMapper
{

    public static UserAccountEntity ToEntity(UserAccount user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserAccountEntity
        {
            Id = user.Id,
            Email = user.Email,
            Role = user.Role,
            Status = user.Status,
            PasswordHash = user.PasswordHash,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }

    public static UserAccount ToDomain(UserAccountEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return UserAccount.Restore(
            entity.Id,
            entity.Email,
            entity.Role,
            entity.PasswordHash,
            entity.Status,
            entity.CreatedAtUtc);
    }

    public static InviteEntity ToEntity(Invite invite)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return new InviteEntity
        {
            Id = invite.Id,
            Email = invite.Email,
            Role = invite.Role,
            TokenHash = invite.TokenHash,
            ExpiresAtUtc = invite.ExpiresAtUtc,
            CreatedByAdminId = invite.CreatedByAdminId,
            CreatedAtUtc = invite.CreatedAtUtc,
            UsedAtUtc = invite.UsedAtUtc
        };
    }

    public static Invite ToDomain(InviteEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var invite = new Invite(
            entity.Id,
            entity.Email,
            entity.Role,
            entity.TokenHash,
            entity.ExpiresAtUtc,
            entity.CreatedByAdminId,
            entity.CreatedAtUtc);

        if (entity.UsedAtUtc.HasValue)
        {
            invite.MarkUsed(entity.UsedAtUtc.Value);
        }

        return invite;
    }

    public static ApiKeyEntity ToEntity(ApiKey apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        return new ApiKeyEntity
        {
            Id = apiKey.Id,
            UserId = apiKey.UserId,
            Name = apiKey.Name,
            Prefix = apiKey.Prefix,
            SecretHash = apiKey.SecretHash,
            RawKey = apiKey.RawKey,
            CreatedAtUtc = apiKey.CreatedAtUtc,
            LastUsedAtUtc = apiKey.LastUsedAtUtc,
            RevokedAtUtc = apiKey.RevokedAtUtc,
            DeletedAtUtc = apiKey.DeletedAtUtc
        };
    }

    public static ApiKey ToDomain(ApiKeyEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var apiKey = new ApiKey(
            entity.Id,
            entity.UserId,
            entity.Name,
            entity.Prefix,
            entity.SecretHash,
            entity.CreatedAtUtc,
            entity.RawKey);

        if (entity.LastUsedAtUtc.HasValue)
        {
            apiKey.MarkUsed(entity.LastUsedAtUtc.Value);
        }

        if (entity.RevokedAtUtc.HasValue)
        {
            apiKey.Revoke(entity.RevokedAtUtc.Value);
        }

        if (entity.DeletedAtUtc.HasValue)
        {
            apiKey.Delete(entity.DeletedAtUtc.Value);
        }

        return apiKey;
    }

    public static ExternalLogin ToDomain(ExternalLoginEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new ExternalLogin(entity.Id, entity.UserId, entity.Provider, entity.ProviderSub, entity.CreatedAtUtc);
    }

    public static EmailVerification ToDomain(EmailVerificationEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new EmailVerification(
            entity.UserId,
            entity.CodeHash,
            entity.ExpiresAtUtc,
            entity.SentAtUtc,
            entity.Attempts);
    }
}
