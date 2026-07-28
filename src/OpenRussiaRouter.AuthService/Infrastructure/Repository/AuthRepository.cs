using Dapper;
using Npgsql;
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
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.invites
                (id, email, role, token_hash, expires_at_utc, created_by_admin_id, created_at_utc, used_at_utc)
            values
                (@Id, @Email, @Role, @TokenHash, @ExpiresAtUtc, @CreatedByAdminId, @CreatedAtUtc, @UsedAtUtc);
            """,
            ToParameters(invite),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<Invite?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<InviteEntity>(new CommandDefinition(
            InviteSelectSql + " where token_hash = @TokenHash;",
            new { TokenHash = tokenHash },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
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
        var npgsqlConn = (NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.users
                (id, email, role, status, password_hash, created_at_utc, display_name, max_api_keys, display_currency)
            values
                (@Id, @Email, @Role, @Status, @PasswordHash, @CreatedAtUtc, @DisplayName, @MaxApiKeys, @DisplayCurrency);
            """,
            ToParameters(user),
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.invites
            set used_at_utc = @UsedAtUtc
            where id = @InviteId
              and used_at_utc is null;
            """,
            new { InviteId = invite.Id, invite.UsedAtUtc },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        if (updated != 1)
            throw new InvalidOperationException("Invite has already been used.");

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<UserAccount?> GetUserAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<UserAccountEntity>(new CommandDefinition(
            UserSelectSql + " where id = @UserId;",
            new { UserId = userId },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<UserAccount?> GetUserByEmailAsync(string email, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<UserAccountEntity>(new CommandDefinition(
            UserSelectSql + " where lower(email) = lower(@Email);",
            new { Email = email },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<(IReadOnlyCollection<UserAccount> Items, bool HasMore)> ListUsersAsync(int limit, string? afterUserId = null, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterUserId != null)
        {
            afterCondition = " where row(created_at_utc, id) < (select created_at_utc, id from auth.users where id = @AfterUserId) ";
            parameters.Add("AfterUserId", afterUserId);
        }

        var items = (await connection.QueryAsync<UserAccountEntity>(new CommandDefinition(
                UserSelectSql + afterCondition + " order by created_at_utc desc, id desc limit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
            .Select(AuthPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task UpdateUserAsync(UserAccount user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.users
            set display_name = @DisplayName,
                max_api_keys = @MaxApiKeys,
                display_currency = @DisplayCurrency
            where id = @Id;
            """,
            ToParameters(user),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> CountApiKeysAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from auth.api_keys where user_id = @UserId and deleted_at_utc is null;",
            new { UserId = userId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task AddApiKeyAsync(ApiKey apiKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.api_keys
                (id, user_id, name, prefix, secret_hash, raw_key, created_at_utc, last_used_at_utc, disabled_at_utc)
            values
                (@Id, @UserId, @Name, @Prefix, @SecretHash, @RawKey, @CreatedAtUtc, @LastUsedAtUtc, @DisabledAtUtc);
            """,
            ToParameters(apiKey),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> TryAddApiKeyAsync(ApiKey apiKey, int maxApiKeys, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        using var connection = connectionFactory.CreateConnection();
        var npgsqlConn = (NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);

        var activeCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from auth.api_keys where user_id = @UserId and deleted_at_utc is null;",
            new { apiKey.UserId },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        if (activeCount >= maxApiKeys)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.api_keys
                (id, user_id, name, prefix, secret_hash, raw_key, created_at_utc, last_used_at_utc, disabled_at_utc)
            values
                (@Id, @UserId, @Name, @Prefix, @SecretHash, @RawKey, @CreatedAtUtc, @LastUsedAtUtc, @DisabledAtUtc);
            """,
            ToParameters(apiKey),
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<ApiKey?> GetApiKeyAsync(string apiKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ApiKeyEntity>(new CommandDefinition(
            ApiKeySelectSql + " where id = @ApiKeyId;",
            new { ApiKeyId = apiKeyId },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<(IReadOnlyCollection<ApiKey> Items, bool HasMore)> ListApiKeysAsync(string userId, int limit, string? afterApiKeyId = null, bool includeDeleted = false, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("Limit", limit + 1);
        
        var afterCondition = "";
        if (afterApiKeyId != null)
        {
            afterCondition = " and row(created_at_utc, id) < (select created_at_utc, id from auth.api_keys where id = @AfterApiKeyId and user_id = @UserId) ";
            parameters.Add("AfterApiKeyId", afterApiKeyId);
        }

        var deletedCondition = includeDeleted ? "" : " and deleted_at_utc is null ";

        var items = (await connection.QueryAsync<ApiKeyEntity>(new CommandDefinition(
                ApiKeySelectSql + " where user_id = @UserId" + deletedCondition + afterCondition + " order by created_at_utc desc, id desc limit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
            .Select(AuthPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<bool> RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.api_keys
            set name = @Name
            where id = @ApiKeyId
              and user_id = @UserId
              and disabled_at_utc is null;
            """,
            new { UserId = userId, ApiKeyId = apiKeyId, Name = name },
            cancellationToken: ct)).ConfigureAwait(false) == 1;
    }

    public async Task<bool> DisableApiKeyAsync(string userId, string apiKeyId, DateTimeOffset disabledAtUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.api_keys
            set disabled_at_utc = coalesce(disabled_at_utc, @DisabledAtUtc)
            where id = @ApiKeyId
              and user_id = @UserId
              and disabled_at_utc is null;
            """,
            new { UserId = userId, ApiKeyId = apiKeyId, DisabledAtUtc = disabledAtUtc },
            cancellationToken: ct)).ConfigureAwait(false) == 1;
    }

    public async Task<bool> EnableApiKeyAsync(string userId, string apiKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.api_keys
            set disabled_at_utc = null
            where id = @ApiKeyId
              and user_id = @UserId
              and disabled_at_utc is not null;
            """,
            new { UserId = userId, ApiKeyId = apiKeyId },
            cancellationToken: ct)).ConfigureAwait(false) == 1;
    }

    public async Task<bool> DeleteApiKeyAsync(string userId, string apiKeyId, DateTimeOffset deletedAtUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.api_keys
            set deleted_at_utc = @DeletedAtUtc
            where id = @ApiKeyId
              and user_id = @UserId
              and disabled_at_utc is not null
              and deleted_at_utc is null;
            """,
            new { UserId = userId, ApiKeyId = apiKeyId, DeletedAtUtc = deletedAtUtc },
            cancellationToken: ct)).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyCollection<(string Prefix, string SecretHash)>> ListActiveApiKeyHashesAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<(string Prefix, string SecretHash)>(new CommandDefinition(
            """
            select prefix as Prefix, secret_hash as SecretHash
            from auth.api_keys
            where disabled_at_utc is null;
            """,
            cancellationToken: ct)).ConfigureAwait(false)).ToArray();
    }

    public async Task<ApiKey?> FindActiveApiKeyBySecretHashAsync(string secretHash, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ApiKeyEntity>(new CommandDefinition(
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
                ak.disabled_at_utc as DisabledAtUtc,
                u.role as "UserRole"
            from auth.api_keys ak
            inner join auth.users u on u.id = ak.user_id
            where ak.secret_hash = @SecretHash
              and ak.disabled_at_utc is null
              and u.status = 'Active'
            limit 1;
            """,
            new { SecretHash = secretHash },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task MarkApiKeyUsedAsync(string apiKeyId, DateTimeOffset lastUsedAtUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.api_keys
            set last_used_at_utc = @LastUsedAtUtc
            where id = @ApiKeyId
              and (last_used_at_utc is null or last_used_at_utc < @LastUsedAtUtc - interval '1 minute');
            """,
            new { ApiKeyId = apiKeyId, LastUsedAtUtc = lastUsedAtUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<string?> GetFirstActiveApiKeyRawAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            """
            select raw_key
            from auth.api_keys
            where user_id = @UserId
              and disabled_at_utc is null
              and raw_key is not null
            order by created_at_utc desc
            limit 1;
            """,
            new { UserId = userId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<(string ApiKeyId, string RawKey, string UserId, string Name)>> ListAllActiveApiKeysRawAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<(string ApiKeyId, string RawKey, string UserId, string Name)>(new CommandDefinition(
            """
            select id as ApiKeyId, raw_key as RawKey, user_id as UserId, name as Name
            from auth.api_keys
            where disabled_at_utc is null
              and raw_key is not null;
            """,
            cancellationToken: ct)).ConfigureAwait(false)).ToArray();
    }

    public async Task<IReadOnlyCollection<(string ApiKeyId, string RawKey, string UserId, string Name, bool IsActive)>> ListAllNonDeletedApiKeysRawAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<(string ApiKeyId, string RawKey, string UserId, string Name, bool IsActive)>(new CommandDefinition(
            """
            select id as ApiKeyId,
                   raw_key as RawKey,
                   user_id as UserId,
                   name as Name,
                   (disabled_at_utc is null) as IsActive
            from auth.api_keys
            where deleted_at_utc is null
              and raw_key is not null;
            """,
            cancellationToken: ct)).ConfigureAwait(false)).ToArray();
    }

    public async Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ExternalLoginEntity>(new CommandDefinition(
            ExternalLoginSelectSql + " where provider = @Provider and provider_sub = @ProviderSub;",
            new { Provider = provider, ProviderSub = providerSub },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(login);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.external_logins
                (id, user_id, provider, provider_sub, created_at_utc)
            values
                (@Id, @UserId, @Provider, @ProviderSub, @CreatedAtUtc);
            """,
            new { login.Id, login.UserId, login.Provider, login.ProviderSub, login.CreatedAtUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(login);
        using var connection = connectionFactory.CreateConnection();
        var npgsqlConn = (NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.users
                (id, email, role, status, password_hash, created_at_utc, display_name, max_api_keys, display_currency)
            values
                (@Id, @Email, @Role, @Status, @PasswordHash, @CreatedAtUtc, @DisplayName, @MaxApiKeys, @DisplayCurrency);
            """,
            ToParameters(user),
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.external_logins
                (id, user_id, provider, provider_sub, created_at_utc)
            values
                (@Id, @UserId, @Provider, @ProviderSub, @CreatedAtUtc);
            """,
            new { login.Id, login.UserId, login.Provider, login.ProviderSub, login.CreatedAtUtc },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task AddUserWithEmailVerificationAsync(UserAccount user, EmailVerification verification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(verification);
        using var connection = connectionFactory.CreateConnection();
        var npgsqlConn = (NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.users
                (id, email, role, status, password_hash, created_at_utc, display_name, max_api_keys, display_currency)
            values
                (@Id, @Email, @Role, @Status, @PasswordHash, @CreatedAtUtc, @DisplayName, @MaxApiKeys, @DisplayCurrency);
            """,
            ToParameters(user),
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.email_verifications
                (user_id, code_hash, expires_at_utc, sent_at_utc, attempts)
            values
                (@UserId, @CodeHash, @ExpiresAtUtc, @SentAtUtc, @Attempts);
            """,
            ToParameters(verification),
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<EmailVerification?> GetEmailVerificationAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<EmailVerificationEntity>(new CommandDefinition(
            EmailVerificationSelectSql + " where user_id = @UserId;",
            new { UserId = userId },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task UpdateEmailVerificationAsync(EmailVerification verification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(verification);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.email_verifications
            set code_hash = @CodeHash,
                expires_at_utc = @ExpiresAtUtc,
                sent_at_utc = @SentAtUtc,
                attempts = @Attempts
            where user_id = @UserId;
            """,
            ToParameters(verification),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task ActivateUserAndDeleteVerificationAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var npgsqlConn = (NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "update auth.users set status = 'Active' where id = @UserId;",
            new { UserId = userId },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "delete from auth.email_verifications where user_id = @UserId;",
            new { UserId = userId },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateLoginThrottleAsync(string userId, int failedLoginCount, DateTimeOffset? lockedUntilUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.users
            set failed_login_count = @FailedLoginCount,
                locked_until_utc = @LockedUntilUtc
            where id = @UserId;
            """,
            new { UserId = userId, FailedLoginCount = failedLoginCount, LockedUntilUtc = lockedUntilUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task UpdatePasswordHashAsync(string userId, string passwordHash, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            PasswordUpdateSql,
            new { UserId = userId, PasswordHash = passwordHash },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task UpsertPasswordResetAsync(EmailVerification reset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reset);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into auth.password_resets
                (user_id, code_hash, expires_at_utc, sent_at_utc, attempts)
            values
                (@UserId, @CodeHash, @ExpiresAtUtc, @SentAtUtc, @Attempts)
            on conflict (user_id) do update
            set code_hash = excluded.code_hash,
                expires_at_utc = excluded.expires_at_utc,
                sent_at_utc = excluded.sent_at_utc,
                attempts = excluded.attempts;
            """,
            ToParameters(reset),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<EmailVerification?> GetPasswordResetAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<EmailVerificationEntity>(new CommandDefinition(
            PasswordResetSelectSql + " where user_id = @UserId;",
            new { UserId = userId },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? AuthPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task UpdatePasswordResetAsync(EmailVerification reset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reset);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.password_resets
            set code_hash = @CodeHash,
                expires_at_utc = @ExpiresAtUtc,
                sent_at_utc = @SentAtUtc,
                attempts = @Attempts
            where user_id = @UserId;
            """,
            ToParameters(reset),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task UpdatePasswordAndDeleteResetAsync(string userId, string passwordHash, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var npgsqlConn = (NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            PasswordUpdateSql,
            new { UserId = userId, PasswordHash = passwordHash },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "delete from auth.password_resets where user_id = @UserId;",
            new { UserId = userId },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static object ToParameters(EmailVerification verification) => new
    {
        verification.UserId,
        verification.CodeHash,
        verification.ExpiresAtUtc,
        verification.SentAtUtc,
        verification.Attempts
    };

    private static object ToParameters(UserAccount user) => new
    {
        user.Id,
        user.Email,
        Role = user.Role.ToString(),
        Status = user.Status.ToString(),
        user.PasswordHash,
        user.CreatedAtUtc,
        user.DisplayName,
        user.MaxApiKeys,
        user.DisplayCurrency
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
        apiKey.DisabledAtUtc
    };

    private const string UserSelectSql = """
        select
            id as Id,
            email as Email,
            role as Role,
            status as Status,
            password_hash as PasswordHash,
            created_at_utc as CreatedAtUtc,
            display_name as DisplayName,
            max_api_keys as MaxApiKeys,
            display_currency as DisplayCurrency,
            failed_login_count as FailedLoginCount,
            locked_until_utc as LockedUntilUtc
        from auth.users
        """;

    private const string PasswordUpdateSql = """
        update auth.users
        set password_hash = @PasswordHash,
            failed_login_count = 0,
            locked_until_utc = null
        where id = @UserId;
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
            disabled_at_utc as DisabledAtUtc,
            deleted_at_utc as DeletedAtUtc
        from auth.api_keys
        """;

    private const string EmailVerificationSelectSql = """
        select
            user_id as UserId,
            code_hash as CodeHash,
            expires_at_utc as ExpiresAtUtc,
            sent_at_utc as SentAtUtc,
            attempts as Attempts
        from auth.email_verifications
        """;

    private const string PasswordResetSelectSql = """
        select
            user_id as UserId,
            code_hash as CodeHash,
            expires_at_utc as ExpiresAtUtc,
            sent_at_utc as SentAtUtc,
            attempts as Attempts
        from auth.password_resets
        """;

    private const string ExternalLoginSelectSql = """
        select
            id as Id,
            user_id as UserId,
            provider as Provider,
            provider_sub as ProviderSub,
            created_at_utc as CreatedAtUtc
        from auth.external_logins
        """;
}
