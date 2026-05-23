using Dapper;
using FluentAssertions;
using Npgsql;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Infrastructure.Persistence;

public sealed class DapperAuthApiKeyValidatorPostgresTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Validate_AppliedMigrationAndActiveKey_ReturnsContext()
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
        var repository = new AuthRepository(new AuthDbConnectionFactory(connectionString));
        var validator = new DapperAuthApiKeyValidator(repository, hasher);

        var result = await validator.ValidateAsync("orr_test_secret");

        result.IsValid.Should().BeTrue();
        result.UserId.Should().Be("user-1");
        result.ApiKeyId.Should().Be("key-1");
        Assert.NotNull(connection.ExecuteScalar<DateTime?>(
            "select last_used_at_utc from auth.api_keys where id = 'key-1';"));
    }

}
