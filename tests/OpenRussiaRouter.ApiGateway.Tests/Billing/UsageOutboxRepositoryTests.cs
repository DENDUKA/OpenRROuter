using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.Infrastructure.Persistence;
using OpenRussiaRouter.ApiGateway.Infrastructure.Repository;
using OpenRussiaRouter.BuildingBlocks.Usage;
using Testcontainers.PostgreSql;

namespace OpenRussiaRouter.ApiGateway.Tests.Billing;

public sealed class UsageOutboxRepositoryTests
{
    [Fact]
    public async Task InMemoryRepository_DuplicateRequestId_LeasesSingleEvent()
    {
        var repository = new InMemoryUsageOutboxRepository(NullLogger<InMemoryUsageOutboxRepository>.Instance);
        var now = DateTimeOffset.UtcNow;

        await repository.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);
        await repository.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);

        var leased = await repository.LeaseDueAsync(10, "worker-1", TimeSpan.FromMinutes(1), now.AddSeconds(1), CancellationToken.None);

        Assert.Single(leased);
        Assert.Equal("req-1", Assert.Single(leased).UsageEvent.RequestId);
    }

    [Fact]
    public async Task InMemoryRepository_WorksWithoutGatewayDb()
    {
        var repository = new InMemoryUsageOutboxRepository(NullLogger<InMemoryUsageOutboxRepository>.Instance);

        await repository.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);
        var leased = await repository.LeaseDueAsync(10, "worker-1", TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow.AddSeconds(1), CancellationToken.None);

        Assert.Single(leased);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task DapperRepository_AppliedMigration_EnqueuesAndLeasesIdempotently()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        database.ApplyMigration();
        var repository = new DapperUsageOutboxRepository(new GatewayDbConnectionFactory(database.ConnectionString));
        var now = DateTimeOffset.UtcNow;

        await repository.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);
        await repository.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);

        var leased = await repository.LeaseDueAsync(10, "worker-1", TimeSpan.FromMinutes(1), now.AddSeconds(1), CancellationToken.None);

        Assert.Single(leased);
        Assert.Equal("req-1", Assert.Single(leased).UsageEvent.RequestId);
    }

    private static GatewayUsageEvent CreateEvent(string requestId)
    {
        return new GatewayUsageEvent(
            requestId,
            "user-1",
            "key-1",
            "gpt-test",
            "node-1",
            new TokenUsage(10, 20, 0),
            "completed",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow);
    }

    private sealed class PostgresTestDatabase : IAsyncDisposable
    {
        private readonly PostgreSqlContainer container;

        private PostgresTestDatabase(PostgreSqlContainer container)
        {
            this.container = container;
        }

        public string ConnectionString => container.GetConnectionString();

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
            catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await container.DisposeAsync();
        }

        public void ApplyMigration()
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            connection.Execute(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "db", "migrations", "gateway", "001_usage_outbox.sql")));
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
