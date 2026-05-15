using Dapper;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;

namespace OpenRussiaRouter.AuthService.Infrastructure.Repository;

public sealed class AuthRepository(IAuthDbConnectionFactory connectionFactory) : IAuthRepository
{

    public void AddInvite(Invite invite)
    {
        ArgumentNullException.ThrowIfNull(invite);
        using var connection = connectionFactory.CreateConnection();
        connection.Execute(
            """
            insert into auth.invites
                (id, email, role, token_hash, expires_at_utc, created_by_admin_id, created_at_utc, used_at_utc)
            values
                (@Id, @Email, @Role, @TokenHash, @ExpiresAtUtc, @CreatedByAdminId, @CreatedAtUtc, @UsedAtUtc);
            """,
            ToParameters(invite));
    }

    public Invite? GetInviteByTokenHash(string tokenHash)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<InviteEntity>(
            InviteSelectSql + " where token_hash = @TokenHash;",
            new { TokenHash = tokenHash }) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public void AddUserAndMarkInviteUsed(UserAccount user, Invite invite)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(invite);
        if (!invite.UsedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Invite must be marked as used before persistence.");
        }

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            """
            insert into auth.users
                (id, email, role, status, password_hash, created_at_utc)
            values
                (@Id, @Email, @Role, @Status, @PasswordHash, @CreatedAtUtc);
            """,
            ToParameters(user),
            transaction);

        var updated = connection.Execute(
            """
            update auth.invites
            set used_at_utc = @UsedAtUtc
            where id = @InviteId
              and used_at_utc is null;
            """,
            new { InviteId = invite.Id, invite.UsedAtUtc },
            transaction);

        if (updated != 1)
        {
            transaction.Rollback();
            throw new InvalidOperationException("Invite has already been used.");
        }

        transaction.Commit();
    }

    public UserAccount? GetUser(string userId)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<UserAccountEntity>(
            UserSelectSql + " where id = @UserId;",
            new { UserId = userId }) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public UserAccount? GetUserByEmail(string email)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<UserAccountEntity>(
            UserSelectSql + " where lower(email) = lower(@Email);",
            new { Email = email }) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public IReadOnlyCollection<UserAccount> ListUsers()
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.Query<UserAccountEntity>(UserSelectSql + " order by created_at_utc desc limit 1000;")
            .Select(AuthPersistenceMapper.ToDomain)
            .ToArray();
    }

    public void AddApiKey(ApiKey apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        using var connection = connectionFactory.CreateConnection();
        connection.Execute(
            """
            insert into auth.api_keys
                (id, user_id, name, prefix, secret_hash, created_at_utc, last_used_at_utc, revoked_at_utc)
            values
                (@Id, @UserId, @Name, @Prefix, @SecretHash, @CreatedAtUtc, @LastUsedAtUtc, @RevokedAtUtc);
            """,
            ToParameters(apiKey));
    }

    public ApiKey? GetApiKey(string apiKeyId)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<ApiKeyEntity>(
            ApiKeySelectSql + " where id = @ApiKeyId;",
            new { ApiKeyId = apiKeyId }) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public IReadOnlyCollection<ApiKey> ListApiKeys(string userId)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.Query<ApiKeyEntity>(
                ApiKeySelectSql + " where user_id = @UserId order by created_at_utc desc limit 1000;",
                new { UserId = userId })
            .Select(AuthPersistenceMapper.ToDomain)
            .ToArray();
    }

    public bool RenameApiKey(string userId, string apiKeyId, string name)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.Execute(
            """
            update auth.api_keys
            set name = @Name
            where id = @ApiKeyId
              and user_id = @UserId
              and revoked_at_utc is null;
            """,
            new { UserId = userId, ApiKeyId = apiKeyId, Name = name }) == 1;
    }

    public bool RevokeApiKey(string userId, string apiKeyId, DateTimeOffset revokedAtUtc)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.Execute(
            """
            update auth.api_keys
            set revoked_at_utc = coalesce(revoked_at_utc, @RevokedAtUtc)
            where id = @ApiKeyId
              and user_id = @UserId
              and revoked_at_utc is null;
            """,
            new { UserId = userId, ApiKeyId = apiKeyId, RevokedAtUtc = revokedAtUtc }) == 1;
    }

    public IReadOnlyCollection<(string Prefix, string SecretHash)> ListActiveApiKeyHashes()
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.Query<(string Prefix, string SecretHash)>(
            """
            select prefix as Prefix, secret_hash as SecretHash
            from auth.api_keys
            where revoked_at_utc is null
            order by created_at_utc desc
            limit 10000;
            """).ToArray();
    }

    private static object ToParameters(UserAccount user)
    {
        return new
        {
            user.Id,
            user.Email,
            Role = user.Role.ToString(),
            Status = user.Status.ToString(),
            user.PasswordHash,
            user.CreatedAtUtc
        };
    }

    private static object ToParameters(Invite invite)
    {
        return new
        {
            invite.Id,
            invite.Email,
            Role = invite.Role.ToString(),
            invite.TokenHash,
            invite.ExpiresAtUtc,
            invite.CreatedByAdminId,
            invite.CreatedAtUtc,
            invite.UsedAtUtc
        };
    }

    private static object ToParameters(ApiKey apiKey)
    {
        return new
        {
            apiKey.Id,
            apiKey.UserId,
            apiKey.Name,
            apiKey.Prefix,
            apiKey.SecretHash,
            apiKey.CreatedAtUtc,
            apiKey.LastUsedAtUtc,
            apiKey.RevokedAtUtc
        };
    }

    private const string UserSelectSql = """
        select
            id as Id,
            email as Email,
            role as Role,
            status as Status,
            password_hash as PasswordHash,
            created_at_utc as CreatedAtUtc
        from auth.users
        """;

    private const string InviteSelectSql = """
        select
            id as Id,
            email as Email,
            role as Role,
            token_hash as TokenHash,
            expires_at_utc as ExpiresAtUtc,
            created_by_admin_id as CreatedByAdminId,
            created_at_utc as CreatedAtUtc,
            used_at_utc as UsedAtUtc
        from auth.invites
        """;

    private const string ApiKeySelectSql = """
        select
            id as Id,
            user_id as UserId,
            name as Name,
            prefix as Prefix,
            secret_hash as SecretHash,
            created_at_utc as CreatedAtUtc,
            last_used_at_utc as LastUsedAtUtc,
            revoked_at_utc as RevokedAtUtc
        from auth.api_keys
        """;
}
