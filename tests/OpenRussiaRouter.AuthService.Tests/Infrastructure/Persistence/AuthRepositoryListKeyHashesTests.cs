using Dapper;
using FluentAssertions;
using Npgsql;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;

namespace OpenRussiaRouter.AuthService.Tests.Infrastructure.Persistence;

public sealed class AuthRepositoryListKeyHashesTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ListActiveApiKeyHashes_ReturnsOnlyNonRevokedKeys()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;

        database.ApplyAuthMigration();
        database.TruncateAuth();
        using var connection = new NpgsqlConnection(database.ConnectionString);

        var now = DateTimeOffset.UtcNow;
        connection.Execute(
            """
            insert into auth.users (id, email, role, status, password_hash, created_at_utc)
            values ('user-1', 'user1@example.com', 'user', 'active', 'x', @Now);
            """, new { Now = now });
        // Insert one active and one disabled key directly
        connection.Execute(
            """
            insert into auth.api_keys (id, user_id, name, prefix, secret_hash, created_at_utc)
            values (@Id, 'user-1', 'active', 'rwt_abc', 'hash-active', @Now);
            """, new { Id = "key-1", Now = now });
        connection.Execute(
            """
            insert into auth.api_keys (id, user_id, name, prefix, secret_hash, created_at_utc, disabled_at_utc)
            values (@Id, 'user-1', 'disabled', 'rwt_xyz', 'hash-disabled', @Now, @DisabledAt);
            """, new { Id = "key-2", Now = now, DisabledAt = now.AddMinutes(1) });

        var repo = new AuthRepository(new AuthDbConnectionFactory(database.ConnectionString));
        var hashes = await repo.ListActiveApiKeyHashesAsync();

        hashes.Should().ContainSingle();
        var entry = hashes.Single();
        entry.Prefix.Should().Be("rwt_abc");
        entry.SecretHash.Should().Be("hash-active");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ListActiveApiKeyHashes_EmptyTable_ReturnsEmptyCollection()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;

        database.ApplyAuthMigration();
        database.TruncateAuth();

        var repo = new AuthRepository(new AuthDbConnectionFactory(database.ConnectionString));
        var hashes = await repo.ListActiveApiKeyHashesAsync();

        hashes.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ListActiveApiKeyHashes_MultipleActiveKeys_ReturnsAllOfThem()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;

        database.ApplyAuthMigration();
        database.TruncateAuth();
        using var connection = new NpgsqlConnection(database.ConnectionString);

        var now = DateTimeOffset.UtcNow;
        connection.Execute(
            """
            insert into auth.users (id, email, role, status, password_hash, created_at_utc)
            values ('user-1', 'user1@example.com', 'user', 'active', 'x', @Now);
            """, new { Now = now });
        for (var i = 1; i <= 5; i++)
        {
            connection.Execute(
                """
                insert into auth.api_keys (id, user_id, name, prefix, secret_hash, created_at_utc)
                values (@Id, 'user-1', @Name, @Prefix, @Hash, @Now);
                """,
                new { Id = $"key-{i}", Name = $"key{i}", Prefix = $"rwt_{i:D3}", Hash = $"hash-{i}", Now = now.AddSeconds(i) });
        }

        var repo = new AuthRepository(new AuthDbConnectionFactory(database.ConnectionString));
        var hashes = await repo.ListActiveApiKeyHashesAsync();

        hashes.Should().HaveCount(5);
        // Verify ordering: most recent first
        hashes.First().Prefix.Should().Be("rwt_005");
    }
}
