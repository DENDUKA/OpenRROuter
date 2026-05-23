using Dapper;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;

namespace OpenRussiaRouter.AuthService.Infrastructure.Repository;

public sealed class AuthRepository(IAuthDbConnectionFactory connectionFactory) : IAuthRepository
{
    public async Task AddInviteAsync(Invite invite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invite);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            insert into auth.invites
                (id, email, role, token_hash, expires_at_utc, created_by_admin_id, created_at_utc, used_at_utc)
            values
                (@Id, @Email, @Role, @TokenHash, @ExpiresAtUtc, @CreatedByAdminId, @CreatedAtUtc, @UsedAtUtc);
            """,
            ToParameters(invite)).ConfigureAwait(false);
    }

    public async Task<Invite?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<InviteEntity>(
            InviteSelectSql + " where token_hash = @TokenHash;",
            new { TokenHash = tokenHash }).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task AddUserAndMarkInviteUsedAsync(UserAccount user, Invite invite, CancellationToken ct = default)
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

        await connection.ExecuteAsync(
            """
            insert into auth.users
                (id, email, role, status, password_hash, created_at_utc)
            values
                (@Id, @Email, @Role, @Status, @PasswordHash, @CreatedAtUtc);
            """,
            ToParameters(user),
            transaction).ConfigureAwait(false);

        var updated = await connection.ExecuteAsync(
            """
            update auth.invites
            set used_at_utc = @UsedAtUtc
            where id = @InviteId
              and used_at_utc is null;
            """,
            new { InviteId = invite.Id, invite.UsedAtUtc },
            transaction).ConfigureAwait(false);

        if (updated != 1)
        {
            transaction.Rollback();
            throw new InvalidOperationException("Invite has already been used.");
        }

        transaction.Commit();
    }

    public async Task<UserAccount?> GetUserAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<UserAccountEntity>(
            UserSelectSql + " where id = @UserId;",
            new { UserId = userId }).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<UserAccount?> GetUserByEmailAsync(string email, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<UserAccountEntity>(
            UserSelectSql + " where lower(email) = lower(@Email);",
            new { Email = email }).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<(IReadOnlyCollection<UserAccount> Items, bool HasMore)> ListUsersAsync(int limit, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<UserAccountEntity>(
                UserSelectSql + " order by created_at_utc desc limit @Limit;",
                new { Limit = limit + 1 }).ConfigureAwait(false))
            .Select(AuthPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task AddApiKeyAsync(ApiKey apiKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            insert into auth.api_keys
                (id, user_id, name, prefix, secret_hash, raw_key, created_at_utc, last_used_at_utc, revoked_at_utc)
            values
                (@Id, @UserId, @Name, @Prefix, @SecretHash, @RawKey, @CreatedAtUtc, @LastUsedAtUtc, @RevokedAtUtc);
            """,
            ToParameters(apiKey)).ConfigureAwait(false);
    }

    public async Task<ApiKey?> GetApiKeyAsync(string apiKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ApiKeyEntity>(
            ApiKeySelectSql + " where id = @ApiKeyId;",
            new { ApiKeyId = apiKeyId }).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<(IReadOnlyCollection<ApiKey> Items, bool HasMore)> ListApiKeysAsync(string userId, int limit, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ApiKeyEntity>(
                ApiKeySelectSql + " where user_id = @UserId order by created_at_utc desc limit @Limit;",
                new { UserId = userId, Limit = limit + 1 }).ConfigureAwait(false))
            .Select(AuthPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<bool> RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            """
            update auth.api_keys
            set name = @Name
            where id = @ApiKeyId
              and user_id = @UserId
              and revoked_at_utc is null;
            """,
            new { UserId = userId, ApiKeyId = apiKeyId, Name = name }).ConfigureAwait(false) == 1;
    }

    public async Task<bool> RevokeApiKeyAsync(string userId, string apiKeyId, DateTimeOffset revokedAtUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            """
            update auth.api_keys
            set revoked_at_utc = coalesce(revoked_at_utc, @RevokedAtUtc)
            where id = @ApiKeyId
              and user_id = @UserId
              and revoked_at_utc is null;
            """,
            new { UserId = userId, ApiKeyId = apiKeyId, RevokedAtUtc = revokedAtUtc }).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyCollection<(string Prefix, string SecretHash)>> ListActiveApiKeyHashesAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<(string Prefix, string SecretHash)>(
            """
            select prefix as Prefix, secret_hash as SecretHash
            from auth.api_keys
            where revoked_at_utc is null
            limit 10000;
            """).ConfigureAwait(false)).ToArray();
    }

    public async Task<ApiKey?> FindActiveApiKeyBySecretHashAsync(string secretHash, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ApiKeyEntity>(
            """
            select
                ak.id as Id,
                ak.user_id as UserId,
                ak.name as Name,
                ak.prefix as Prefix,
                ak.secret_hash as SecretHash,
                ak.raw_key as RawKey,
                ak.created_at_utc as CreatedAtUtc,
                ak.last_used_at_utc as LastUsedAtUtc,
                ak.revoked_at_utc as RevokedAtUtc
            from auth.api_keys ak
            inner join auth.users u on u.id = ak.user_id
            where ak.secret_hash = @SecretHash
              and ak.revoked_at_utc is null
              and u.status = 'Active'
            limit 1;
            """,
            new { SecretHash = secretHash }).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task MarkApiKeyUsedAsync(string apiKeyId, DateTimeOffset lastUsedAtUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            update auth.api_keys
            set last_used_at_utc = @LastUsedAtUtc
            where id = @ApiKeyId
              and (last_used_at_utc is null or last_used_at_utc < @LastUsedAtUtc - interval '1 minute');
            """,
            new { ApiKeyId = apiKeyId, LastUsedAtUtc = lastUsedAtUtc }).ConfigureAwait(false);
    }

    public async Task<string?> GetFirstActiveApiKeyRawAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<string?>(
            """
            select raw_key
            from auth.api_keys
            where user_id = @UserId
              and revoked_at_utc is null
              and raw_key is not null
            order by created_at_utc desc
            limit 1;
            """,
            new { UserId = userId }).ConfigureAwait(false);
    }

    private static object ToParameters(UserAccount user) => new
    {
        user.Id,
        user.Email,
        Role = user.Role.ToString(),
        Status = user.Status.ToString(),
        user.PasswordHash,
        user.CreatedAtUtc
    };

    private static object ToParameters(Invite invite) => new
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

    private static object ToParameters(ApiKey apiKey) => new
    {
        apiKey.Id,
        apiKey.UserId,
        apiKey.Name,
        apiKey.Prefix,
        apiKey.SecretHash,
        apiKey.RawKey,
        apiKey.CreatedAtUtc,
        apiKey.LastUsedAtUtc,
        apiKey.RevokedAtUtc
    };

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
            raw_key as RawKey,
            created_at_utc as CreatedAtUtc,
            last_used_at_utc as LastUsedAtUtc,
            revoked_at_utc as RevokedAtUtc
        from auth.api_keys
        """;

    public Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default)
        => throw new NotImplementedException();
}
