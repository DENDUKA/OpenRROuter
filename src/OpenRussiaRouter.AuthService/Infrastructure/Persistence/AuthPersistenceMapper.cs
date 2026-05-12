using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

/// <summary>
/// Maps Auth domain objects to PostgreSQL persistence rows and back.
/// </summary>
public static class AuthPersistenceMapper
{
    /// <summary>
    /// Converts a domain user to a persistence entity.
    /// </summary>
    /// <param name="user">Domain user.</param>
    /// <returns>Persistence entity.</returns>
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

    /// <summary>
    /// Converts a persisted user to the domain model.
    /// </summary>
    /// <param name="entity">Persistence entity.</param>
    /// <returns>Domain user.</returns>
    public static UserAccount ToDomain(UserAccountEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var user = new UserAccount(entity.Id, entity.Email, entity.Role, entity.PasswordHash, entity.CreatedAtUtc);
        if (entity.Status == UserStatus.Disabled)
        {
            user.Disable();
        }

        return user;
    }

    /// <summary>
    /// Converts a domain invite to a persistence entity.
    /// </summary>
    /// <param name="invite">Domain invite.</param>
    /// <returns>Persistence entity.</returns>
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

    /// <summary>
    /// Converts a persisted invite to the domain model.
    /// </summary>
    /// <param name="entity">Persistence entity.</param>
    /// <returns>Domain invite.</returns>
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

    /// <summary>
    /// Converts a domain API key to a persistence entity.
    /// </summary>
    /// <param name="apiKey">Domain API key.</param>
    /// <returns>Persistence entity.</returns>
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
            CreatedAtUtc = apiKey.CreatedAtUtc,
            LastUsedAtUtc = apiKey.LastUsedAtUtc,
            RevokedAtUtc = apiKey.RevokedAtUtc
        };
    }

    /// <summary>
    /// Converts a persisted API key to the domain model.
    /// </summary>
    /// <param name="entity">Persistence entity.</param>
    /// <returns>Domain API key.</returns>
    public static ApiKey ToDomain(ApiKeyEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var apiKey = new ApiKey(
            entity.Id,
            entity.UserId,
            entity.Name,
            entity.Prefix,
            entity.SecretHash,
            entity.CreatedAtUtc);

        if (entity.LastUsedAtUtc.HasValue)
        {
            apiKey.MarkUsed(entity.LastUsedAtUtc.Value);
        }

        if (entity.RevokedAtUtc.HasValue)
        {
            apiKey.Revoke(entity.RevokedAtUtc.Value);
        }

        return apiKey;
    }
}
