using Dapper;
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
        // Insert one active and one revoked key directly
        connection.Execute(
            """
            insert into auth.api_keys (id, user_id, name, prefix, secret_hash, created_at_utc)
            values (@Id, 'user-1', 'active', 'orr_abc', 'hash-active', @Now);
            """, new { Id = "key-1", Now = now });
        connection.Execute(
            """
            insert into auth.api_keys (id, user_id, name, prefix, secret_hash, created_at_utc, revoked_at_utc)
            values (@Id, 'user-1', 'revoked', 'orr_xyz', 'hash-revoked', @Now, @RevokedAt);
            """, new { Id = "key-2", Now = now, RevokedAt = now.AddMinutes(1) });

        var repo = new AuthRepository(new AuthDbConnectionFactory(database.ConnectionString));
        var hashes = repo.ListActiveApiKeyHashes();

        Assert.Single(hashes);
        var entry = hashes.Single();
        Assert.Equal("orr_abc", entry.Prefix);
        Assert.Equal("hash-active", entry.SecretHash);
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
        var hashes = repo.ListActiveApiKeyHashes();

        Assert.Empty(hashes);
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
        for (var i = 1; i <= 5; i++)
        {
            connection.Execute(
                """
                insert into auth.api_keys (id, user_id, name, prefix, secret_hash, created_at_utc)
                values (@Id, 'user-1', @Name, @Prefix, @Hash, @Now);
                """,
                new { Id = $"key-{i}", Name = $"key{i}", Prefix = $"orr_{i:D3}", Hash = $"hash-{i}", Now = now.AddSeconds(i) });
        }

        var repo = new AuthRepository(new AuthDbConnectionFactory(database.ConnectionString));
        var hashes = repo.ListActiveApiKeyHashes();

        Assert.Equal(5, hashes.Count);
        // Verify ordering: most recent first
        Assert.Equal("orr_005", hashes.First().Prefix);
    }
}
