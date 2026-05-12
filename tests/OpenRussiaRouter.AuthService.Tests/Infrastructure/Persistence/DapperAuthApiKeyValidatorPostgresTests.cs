using Dapper;
using Npgsql;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;
using OpenRussiaRouter.AuthService.Security;

namespace OpenRussiaRouter.AuthService.Tests.Infrastructure.Persistence;

public sealed class DapperAuthApiKeyValidatorPostgresTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public void Validate_AppliedMigrationAndActiveKey_ReturnsContext()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        ApplyMigration(connectionString, "auth");
        using var connection = new NpgsqlConnection(connectionString);
        connection.Execute("truncate auth.api_keys, auth.invites, auth.users restart identity cascade;");
        var hasher = new Sha256SecretHasher();
        connection.Execute(
            """
            insert into auth.users (id, email, role, status, password_hash, created_at_utc)
            values ('user-1', 'user@example.com', 'User', 'Active', 'hash', @NowUtc);

            insert into auth.api_keys
                (id, user_id, name, prefix, secret_hash, created_at_utc)
            values
                ('key-1', 'user-1', 'default', 'orr_test', @SecretHash, @NowUtc);
            """,
            new { NowUtc = DateTimeOffset.UtcNow, SecretHash = hasher.Hash("orr_test_secret") });
        var validator = new DapperAuthApiKeyValidator(
            new NpgsqlAuthDbConnectionFactory(connectionString),
            hasher);

        var result = validator.Validate("orr_test_secret");

        Assert.True(result.IsValid);
        Assert.Equal("user-1", result.UserId);
        Assert.Equal("key-1", result.ApiKeyId);
        Assert.NotNull(connection.ExecuteScalar<DateTimeOffset?>(
            "select last_used_at_utc from auth.api_keys where id = 'key-1';"));
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
