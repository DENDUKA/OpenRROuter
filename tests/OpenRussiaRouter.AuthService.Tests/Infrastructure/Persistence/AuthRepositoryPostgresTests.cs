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
        invite.MarkUsed(DateTimeOffset.UtcNow);

        await repository.AddInviteAsync(invite);
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
            new EmailVerificationService(secretHasher),
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
            new EmailVerificationService(secretHasher),
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
}
