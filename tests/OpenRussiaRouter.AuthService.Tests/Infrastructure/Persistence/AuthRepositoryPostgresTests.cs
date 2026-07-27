using Dapper;
using FluentAssertions;
using Npgsql;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;

namespace OpenRussiaRouter.AuthService.Tests.Infrastructure.Persistence;

public sealed class AuthRepositoryPostgresTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ListUsersAsync_UsesCreatedAtAndIdAsTheCursorKey()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        database.ApplyAuthMigration();
        database.TruncateAuth();
        var repository = new AuthRepository(new AuthDbConnectionFactory(database.ConnectionString));
        var createdAt = DateTimeOffset.UtcNow;
        var oldest = new UserAccount("z-user", "oldest@example.com", UserRole.User, null, createdAt);
        var middle = new UserAccount("a-user", "middle@example.com", UserRole.User, null, createdAt.AddMinutes(1));
        var newest = new UserAccount("m-user", "newest@example.com", UserRole.User, null, createdAt.AddMinutes(2));

        await AddUserAsync(repository, oldest);
        await AddUserAsync(repository, middle);
        await AddUserAsync(repository, newest);

        var firstPage = await repository.ListUsersAsync(limit: 2);
        var secondPage = await repository.ListUsersAsync(limit: 2, afterUserId: firstPage.Items.Last().Id);

        firstPage.Items.Select(user => user.Id).Should().Equal("m-user", "a-user");
        secondPage.Items.Select(user => user.Id).Should().Equal("z-user");
        firstPage.Items.Concat(secondPage.Items).Select(user => user.Id)
            .Should().OnlyHaveUniqueItems().And.Equal("m-user", "a-user", "z-user");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ListApiKeysAsync_UsesCreatedAtAndIdAsTheCursorKey()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        database.ApplyAuthMigration();
        database.TruncateAuth();
        var repository = new AuthRepository(new AuthDbConnectionFactory(database.ConnectionString));
        var createdAt = DateTimeOffset.UtcNow;
        var user = new UserAccount("pagination-user", "pagination@example.com", UserRole.User, null, createdAt);
        await AddUserAsync(repository, user);

        await repository.AddApiKeyAsync(new ApiKey("z-key", user.Id, "oldest", "sk-or-oldest", "hash-oldest", createdAt));
        await repository.AddApiKeyAsync(new ApiKey("a-key", user.Id, "middle", "sk-or-middle", "hash-middle", createdAt.AddMinutes(1)));
        await repository.AddApiKeyAsync(new ApiKey("m-key", user.Id, "newest", "sk-or-newest", "hash-newest", createdAt.AddMinutes(2)));

        var firstPage = await repository.ListApiKeysAsync(user.Id, limit: 2);
        var secondPage = await repository.ListApiKeysAsync(user.Id, limit: 2, afterApiKeyId: firstPage.Items.Last().Id);

        firstPage.Items.Select(key => key.Id).Should().Equal("m-key", "a-key");
        secondPage.Items.Select(key => key.Id).Should().Equal("z-key");
        firstPage.Items.Concat(secondPage.Items).Select(key => key.Id)
            .Should().OnlyHaveUniqueItems().And.Equal("m-key", "a-key", "z-key");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task UpdateUser_DisplayCurrency_PersistsAndReadsProfileFields()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        var connectionString = database.ConnectionString;
        database.ApplyAuthMigration();
        database.TruncateAuth();
        var repository = new AuthRepository(new AuthDbConnectionFactory(connectionString));
        var user = new UserAccount(
            "currency-user",
            "currency@example.com",
            UserRole.User,
            passwordHash: null,
            DateTimeOffset.UtcNow);
        var invite = new Invite(
            "currency-invite",
            user.Email,
            UserRole.User,
            "token-hash",
            DateTimeOffset.UtcNow.AddDays(1),
            "admin-1",
            DateTimeOffset.UtcNow);

        // The invite row must be stored unused: AddUserAndMarkInviteUsedAsync only
        // updates rows where used_at_utc is still null.
        await repository.AddInviteAsync(invite);
        invite.MarkUsed(DateTimeOffset.UtcNow);
        await repository.AddUserAndMarkInviteUsedAsync(user, invite);

        var created = await repository.GetUserAsync(user.Id);
        created.Should().NotBeNull();
        created!.DisplayCurrency.Should().Be("RUB");

        created.UpdateDetails("Currency User", 5, "EUR");
        await repository.UpdateUserAsync(created);

        var updated = await repository.GetUserAsync(user.Id);
        updated.Should().NotBeNull();
        updated!.DisplayName.Should().Be("Currency User");
        updated.MaxApiKeys.Should().Be(5);
        updated.DisplayCurrency.Should().Be("EUR");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Repository_AppliedMigration_PersistsInviteUserAndApiKeyLifecycle()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        var connectionString = database.ConnectionString;
        database.ApplyAuthMigration();
        using var connection = new NpgsqlConnection(connectionString);
        database.TruncateAuth();
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        var service = new AuthAccountApplicationService(
            new AuthRepository(new AuthDbConnectionFactory(connectionString)),
            new InviteRegistrationService(secretHasher, new Pbkdf2PasswordHasher(), secretGenerator),
            new EmailVerificationService(HmacSecretHasher.CreateDev()),
            new Pbkdf2PasswordHasher(),
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
        var now = DateTimeOffset.UtcNow;

        var invite = await service.CreateInviteAsync("user@example.com", UserRole.User, "admin-1", now.AddDays(1), now);
        var user = await service.CompleteInviteAsync(invite.RawToken, "StrongPassword123!", now.AddMinutes(1));
        var apiKeyResult = await service.CreateApiKeyAsync(user.Id, "default", now.AddMinutes(2));
        var apiKey = apiKeyResult.Created!;
        await service.RenameApiKeyAsync(user.Id, apiKey.ApiKey.Id, "renamed");
        await service.DisableApiKeyAsync(user.Id, apiKey.ApiKey.Id, now.AddMinutes(3));

        Assert.Equal(1, connection.ExecuteScalar<int>("select count(*) from auth.users;"));
        Assert.NotNull(connection.ExecuteScalar<DateTime?>("select used_at_utc from auth.invites limit 1;"));
        Assert.Equal("renamed", connection.ExecuteScalar<string>("select name from auth.api_keys limit 1;"));
        Assert.NotNull(connection.ExecuteScalar<DateTime?>("select disabled_at_utc from auth.api_keys limit 1;"));
        Assert.Equal(0, connection.ExecuteScalar<int>(
            "select count(*) from auth.api_keys where secret_hash = @RawKey;",
            new { apiKey.RawKey }));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task DeleteApiKey_DisabledKey_SoftDeletesAndHidesFromList()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        var connectionString = database.ConnectionString;
        database.ApplyAuthMigration();
        using var connection = new NpgsqlConnection(connectionString);
        database.TruncateAuth();
        var repository = new AuthRepository(new AuthDbConnectionFactory(connectionString));
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        var service = new AuthAccountApplicationService(
            repository,
            new InviteRegistrationService(secretHasher, new Pbkdf2PasswordHasher(), secretGenerator),
            new EmailVerificationService(HmacSecretHasher.CreateDev()),
            new Pbkdf2PasswordHasher(),
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
        var now = DateTimeOffset.UtcNow;

        var invite = await service.CreateInviteAsync("user@example.com", UserRole.User, "admin-1", now.AddDays(1), now);
        var user = await service.CompleteInviteAsync(invite.RawToken, "StrongPassword123!", now.AddMinutes(1));
        var apiKeyResult = await service.CreateApiKeyAsync(user.Id, "default", now.AddMinutes(2));
        var apiKey = apiKeyResult.Created!;

        // Cannot delete an active (not yet disabled) key.
        (await repository.DeleteApiKeyAsync(user.Id, apiKey.ApiKey.Id, now.AddMinutes(3))).Should().BeFalse();

        await service.DisableApiKeyAsync(user.Id, apiKey.ApiKey.Id, now.AddMinutes(4));

        // Deleting a disabled key succeeds and is idempotent (second call returns false).
        (await repository.DeleteApiKeyAsync(user.Id, apiKey.ApiKey.Id, now.AddMinutes(5))).Should().BeTrue();
        (await repository.DeleteApiKeyAsync(user.Id, apiKey.ApiKey.Id, now.AddMinutes(6))).Should().BeFalse();

        Assert.NotNull(connection.ExecuteScalar<DateTime?>("select deleted_at_utc from auth.api_keys limit 1;"));
        var listed = await service.ListApiKeysAsync(user.Id);
        listed.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task PasswordResetAndLoginThrottle_RoundTripThroughPostgres()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        var connectionString = database.ConnectionString;
        database.ApplyAuthMigration();
        database.TruncateAuth();
        var repository = new AuthRepository(new AuthDbConnectionFactory(connectionString));
        var passwordHasher = new Pbkdf2PasswordHasher();
        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount(
            "reset-user",
            "reset@example.com",
            UserRole.User,
            passwordHasher.HashPassword("StrongPassword123!"),
            now);
        var invite = new Invite("reset-invite", user.Email, UserRole.User, "token-hash", now.AddDays(1), "admin-1", now);
        await repository.AddInviteAsync(invite);
        invite.MarkUsed(now);
        await repository.AddUserAndMarkInviteUsedAsync(user, invite);

        // Login throttle fields persist and read back.
        var lockedUntil = now.AddMinutes(15);
        await repository.UpdateLoginThrottleAsync(user.Id, 3, lockedUntil);
        var throttled = await repository.GetUserAsync(user.Id);
        throttled!.FailedLoginCount.Should().Be(3);
        throttled.LockedUntilUtc.Should().BeCloseTo(lockedUntil, TimeSpan.FromSeconds(1));

        // Password reset row upserts (create + replace) and reads back.
        var reset = new EmailVerification(user.Id, "hash-1", now.AddMinutes(15), now);
        await repository.UpsertPasswordResetAsync(reset);
        var reissued = new EmailVerification(user.Id, "hash-2", now.AddMinutes(30), now.AddMinutes(1), attempts: 2);
        await repository.UpsertPasswordResetAsync(reissued);
        var stored = await repository.GetPasswordResetAsync(user.Id);
        stored!.CodeHash.Should().Be("hash-2");
        stored.Attempts.Should().Be(2);

        // Completing the reset swaps the hash, clears the throttle and removes the row atomically.
        var newHash = passwordHasher.HashPassword("EvenStrongerPassword456!");
        await repository.UpdatePasswordAndDeleteResetAsync(user.Id, newHash);
        var updated = await repository.GetUserAsync(user.Id);
        updated!.PasswordHash.Should().Be(newHash);
        updated.FailedLoginCount.Should().Be(0);
        updated.LockedUntilUtc.Should().BeNull();
        (await repository.GetPasswordResetAsync(user.Id)).Should().BeNull();
    }

    private static async Task AddUserAsync(AuthRepository repository, UserAccount user)
    {
        var invite = new Invite(
            $"invite-{user.Id}",
            user.Email,
            user.Role,
            $"token-{user.Id}",
            user.CreatedAtUtc.AddDays(1),
            "admin-1",
            user.CreatedAtUtc);
        await repository.AddInviteAsync(invite);
        invite.MarkUsed(user.CreatedAtUtc);
        await repository.AddUserAndMarkInviteUsedAsync(user, invite);
    }
}
