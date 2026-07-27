using Dapper;
using FluentAssertions;
using Npgsql;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.BuildingBlocks.Usage;
using Testcontainers.PostgreSql;

namespace OpenRussiaRouter.ApiGateway.Tests.Billing;

public sealed class PostgresUsageOutboxRepositoryTests : IAsyncLifetime
{
    private PostgresTestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresTestDatabase.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task EnqueueAsync_Idempotent_DoesNotDuplicate()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigrations();
        var repo = CreateRepository();

        var ev = CreateEvent("req-1");
        await repo.EnqueueAsync(ev, CancellationToken.None);
        await repo.EnqueueAsync(ev, CancellationToken.None);

        using var connection = new NpgsqlConnection(_database.ConnectionString);
        var count = await connection.ExecuteScalarAsync<int>("select count(*) from gateway.usage_outbox;");
        count.Should().Be(1);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("pending")]
    [InlineData("processing")]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("usage_missing")]
    public async Task EnqueueAsync_AllowedStatus_Persists(string status)
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigrations();
        var repo = CreateRepository();
        await repo.EnqueueAsync(CreateEvent("req-" + status) with { Status = status }, CancellationToken.None);

        using var connection = new NpgsqlConnection(_database.ConnectionString);
        var persisted = await connection.ExecuteScalarAsync<string>(
            "select status from gateway.usage_outbox where request_id = @RequestId;",
            new { RequestId = "req-" + status });

        persisted.Should().Be(status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ClaimPendingAsync_NotReady_ReturnsEmpty()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigrations();
        var repo = CreateRepository();
        await repo.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);

        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var claimed = await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);

        claimed.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ClaimPendingAsync_LeasedNotExpired_ReturnsEmpty()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigrations();
        var repo = CreateRepository();
        await repo.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);
        var secondClaim = await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);

        secondClaim.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ClaimPendingAsync_RoundTripsTokenUsageAndFields()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigrations();
        var repo = CreateRepository();

        var ev = CreateEvent("req-1") with { Usage = new TokenUsage(11, 7, 3) };
        await repo.EnqueueAsync(ev, CancellationToken.None);

        var claimed = await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow, CancellationToken.None);

        claimed.Should().ContainSingle();
        var record = claimed[0];
        record.Usage.Should().Be(new TokenUsage(11, 7, 3));
        record.Endpoint.Should().Be("chat_completions");
        record.Stream.Should().BeFalse();
        record.Status.Should().Be("completed");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task MarkSentAsync_DeletesRecord()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigrations();
        var repo = CreateRepository();
        await repo.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);

        await repo.MarkSentAsync("req-1", CancellationToken.None);

        using var connection = new NpgsqlConnection(_database.ConnectionString);
        var count = await connection.ExecuteScalarAsync<int>("select count(*) from gateway.usage_outbox;");
        count.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task MarkFailedAsync_UpdatesAttemptsAndNextAttemptAndClearsLease()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigrations();
        var repo = CreateRepository();
        await repo.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);

        var nextAttempt = now.Add(TimeSpan.FromMinutes(5));
        await repo.MarkFailedAsync("req-1", attempts: 3, nextAttempt, CancellationToken.None);

        using var connection = new NpgsqlConnection(_database.ConnectionString);
        var row = await connection.QuerySingleAsync<(int Attempts, DateTimeOffset NextAttemptAtUtc, DateTimeOffset? LeasedUntilUtc)>(
            "select attempts, next_attempt_at_utc, leased_until_utc from gateway.usage_outbox where request_id = 'req-1';");

        row.Attempts.Should().Be(3);
        row.NextAttemptAtUtc.Should().BeCloseTo(nextAttempt, TimeSpan.FromSeconds(1));
        row.LeasedUntilUtc.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ClaimPendingAsync_ConcurrentWorkers_DoNotClaimSameRow()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigrations();
        var repo = CreateRepository();
        await repo.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var task1 = repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);
        var task2 = repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);
        var results = await Task.WhenAll(task1, task2);

        var totalClaimed = results.Sum(r => r.Count);
        totalClaimed.Should().Be(1);
    }

    private PostgresUsageOutboxRepository CreateRepository()
    {
        var factory = new GatewayDbConnectionFactory(_database!.ConnectionString);
        return new PostgresUsageOutboxRepository(factory);
    }

    private static GatewayUsageEvent CreateEvent(string requestId)
    {
        var now = DateTimeOffset.UtcNow;
        return new GatewayUsageEvent(
            requestId,
            "user-1",
            "key-1",
            "sk-or-test",
            "gpt-4",
            "node-1",
            new TokenUsage(10, 5, 0),
            "completed",
            now.AddSeconds(-1),
            now,
            ProviderName: "prov",
            ProviderAccount: "acct",
            Endpoint: "chat_completions",
            Stream: false);
    }

    private sealed class PostgresTestDatabase : IAsyncDisposable
    {
        private readonly PostgreSqlContainer _container;

        private PostgresTestDatabase(PostgreSqlContainer container)
        {
            _container = container;
        }

        public string ConnectionString => _container.GetConnectionString();

        public static async Task<PostgresTestDatabase?> StartAsync()
        {
            try
            {
                var container = new PostgreSqlBuilder()
                    .WithImage("postgres:16-alpine")
                    .WithDatabase("orr_tests")
                    .WithUsername("postgres")
                    .WithPassword("postgres")
                    .Build();
                var database = new PostgresTestDatabase(container);
                await container.StartAsync();
                return database;
            }
            catch (Exception ex) when (ex is TimeoutException || (ex is ArgumentException arg && arg.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
        }

        public void ApplyMigrations()
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            var migrationDirectory = Path.Combine(
                FindRepositoryRoot(),
                "db",
                "migrations",
                "gateway");

            foreach (var migrationPath in Directory.EnumerateFiles(migrationDirectory, "*.sql")
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                connection.Execute(File.ReadAllText(migrationPath));
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _container.DisposeAsync();
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
}
