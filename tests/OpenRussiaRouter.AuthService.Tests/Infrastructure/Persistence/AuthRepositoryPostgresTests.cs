using Dapper;
using Npgsql;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;

namespace OpenRussiaRouter.AuthService.Tests.Infrastructure.Persistence;

public sealed class AuthRepositoryPostgresTests
{
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
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
        var now = DateTimeOffset.UtcNow;

        var invite = service.CreateInvite("user@example.com", UserRole.User, "admin-1", now.AddDays(1), now);
        var user = service.CompleteInvite(invite.RawToken, "StrongPassword123!", now.AddMinutes(1));
        var apiKey = service.CreateApiKey(user.Id, "default", now.AddMinutes(2));
        service.RenameApiKey(user.Id, apiKey.ApiKey.Id, "renamed");
        service.RevokeApiKey(user.Id, apiKey.ApiKey.Id, now.AddMinutes(3));

        Assert.Equal(1, connection.ExecuteScalar<int>("select count(*) from auth.users;"));
        Assert.NotNull(connection.ExecuteScalar<DateTime?>("select used_at_utc from auth.invites limit 1;"));
        Assert.Equal("renamed", connection.ExecuteScalar<string>("select name from auth.api_keys limit 1;"));
        Assert.NotNull(connection.ExecuteScalar<DateTime?>("select revoked_at_utc from auth.api_keys limit 1;"));
        Assert.Equal(0, connection.ExecuteScalar<int>(
            "select count(*) from auth.api_keys where secret_hash = @RawKey;",
            new { apiKey.RawKey }));
    }

}
