using Dapper;
using Npgsql;
using OpenRussiaRouter.AuthService.Application;
using OpenRussiaRouter.AuthService.Domain;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;
using OpenRussiaRouter.AuthService.Security;

namespace OpenRussiaRouter.AuthService.Tests.Infrastructure.Persistence;

public sealed class DapperAuthRepositoryPostgresTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public void Repository_AppliedMigration_PersistsInviteUserAndApiKeyLifecycle()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        ApplyMigration(connectionString, "auth");
        using var connection = new NpgsqlConnection(connectionString);
        connection.Execute("truncate auth.api_keys, auth.invites, auth.users restart identity cascade;");
        var secretHasher = new Sha256SecretHasher();
        var service = new AuthAccountApplicationService(
            new DapperAuthRepository(new NpgsqlAuthDbConnectionFactory(connectionString)),
            new InviteRegistrationService(secretHasher, new Pbkdf2PasswordHasher()),
            new ApiKeyFactory(secretHasher),
            secretHasher);
        var now = DateTimeOffset.UtcNow;

        var invite = service.CreateInvite("user@example.com", UserRole.User, "admin-1", now.AddDays(1), now);
        var user = service.CompleteInvite(invite.RawToken, "StrongPassword123!", now.AddMinutes(1));
        var apiKey = service.CreateApiKey(user.Id, "default", now.AddMinutes(2));
        service.RenameApiKey(user.Id, apiKey.ApiKey.Id, "renamed");
        service.RevokeApiKey(user.Id, apiKey.ApiKey.Id, now.AddMinutes(3));

        Assert.Equal(1, connection.ExecuteScalar<int>("select count(*) from auth.users;"));
        Assert.NotNull(connection.ExecuteScalar<DateTimeOffset?>("select used_at_utc from auth.invites limit 1;"));
        Assert.Equal("renamed", connection.ExecuteScalar<string>("select name from auth.api_keys limit 1;"));
        Assert.NotNull(connection.ExecuteScalar<DateTimeOffset?>("select revoked_at_utc from auth.api_keys limit 1;"));
        Assert.Equal(0, connection.ExecuteScalar<int>(
            "select count(*) from auth.api_keys where secret_hash = @RawKey;",
            new { apiKey.RawKey }));
    }

    private static string? GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("ORR_AUTH_POSTGRES_TEST_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ORR_POSTGRES_TEST_CONNECTION");
    }

    private static void ApplyMigration(string connectionString, string service)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Execute(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "db", "migrations", service, "001_initial.sql")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenRussiaRouter.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
