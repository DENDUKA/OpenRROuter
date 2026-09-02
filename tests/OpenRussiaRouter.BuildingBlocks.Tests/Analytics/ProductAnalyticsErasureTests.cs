using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using Testcontainers.PostgreSql;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

public sealed class ProductAnalyticsErasureTests : IAsyncLifetime
{
    private const string Salt = "test-salt";
    private const string EnvironmentLabel = "test";

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
    public async Task PurgeUserAsync_DeletesOnlyTargetUserEvents()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigration();

        var services = BuildServices(_database.ConnectionString);
        var sink = services.GetRequiredService<IProductAnalytics>();
        var queue = services.GetRequiredService<ProductAnalyticsEventQueue>();
        var worker = services.GetRequiredService<ProductAnalyticsFlushWorker>();
        var erasure = services.GetRequiredService<IProductAnalyticsErasure>();
        var identities = services.GetRequiredService<IYandexMetrikaIdentityStore>();

        await worker.StartAsync(CancellationToken.None);

        sink.UserVerified("user-1", "email");
        sink.ApiKeyCreated("user-1");
        sink.ApiRequestCompleted("user-1", new ApiRequestAnalytics("gpt-4", 200, 100, 200, 150, "usage-1"));
        sink.UserVerified("user-2", "email");
        sink.ApiKeyCreated("user-2");
        await identities.UpsertAsync("user-1", "123456789", DateTimeOffset.UtcNow);
        await identities.UpsertAsync("user-2", "987654321", DateTimeOffset.UtcNow);

        queue.Writer.Complete();
        await worker.ExecuteTask!;
        await worker.StopAsync(CancellationToken.None);

        var deleted = await erasure.PurgeUserAsync("user-1");

        deleted.Should().Be(4);
        (await identities.GetClientIdAsync("user-1")).Should().BeNull();
        (await identities.GetClientIdAsync("user-2")).Should().Be("987654321");

        using var connection = new NpgsqlConnection(_database.ConnectionString);
        var remaining = (await connection.QueryAsync<string>(
            "select distinct_id from analytics.events order by id"))
            .ToList();

        remaining.Should().HaveCount(2);
        remaining.Should().AllSatisfy(id => id.Should().NotBe(GetDistinctId("user-1")));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task IdentityStore_UpsertAsync_KeepsNewestObservation()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigration();
        var identities = BuildServices(_database.ConnectionString)
            .GetRequiredService<IYandexMetrikaIdentityStore>();
        var observedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

        await identities.UpsertAsync("user-1", "222", observedAt);
        await identities.UpsertAsync("user-1", "111", observedAt.AddMinutes(-1));
        await identities.UpsertAsync("user-1", "333", observedAt.AddMinutes(1));

        (await identities.GetClientIdAsync("user-1")).Should().Be("333");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task PurgeUserAsync_DistinctIdMatchesEmissionPath()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigration();

        var services = BuildServices(_database.ConnectionString);
        var sink = services.GetRequiredService<IProductAnalytics>();
        var queue = services.GetRequiredService<ProductAnalyticsEventQueue>();
        var worker = services.GetRequiredService<ProductAnalyticsFlushWorker>();
        var erasure = services.GetRequiredService<IProductAnalyticsErasure>();

        await worker.StartAsync(CancellationToken.None);

        sink.UserVerified("user-42", "email");

        queue.Writer.Complete();
        await worker.ExecuteTask!;
        await worker.StopAsync(CancellationToken.None);

        var deleted = await erasure.PurgeUserAsync("user-42");

        deleted.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task PurgeUserAsync_NonExistentUser_ReturnsZero()
    {
        if (_database is null)
        {
            return;
        }

        _database.ApplyMigration();

        var services = BuildServices(_database.ConnectionString);
        var erasure = services.GetRequiredService<IProductAnalyticsErasure>();

        var deleted = await erasure.PurgeUserAsync("nonexistent-user");

        deleted.Should().Be(0);
    }

    [Fact]
    public void DisabledAnalytics_ResolvesNoOpErasure()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "false",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddProductAnalytics(configuration);

        var services = serviceCollection.BuildServiceProvider();
        var erasure = services.GetRequiredService<IProductAnalyticsErasure>();
        var identities = services.GetRequiredService<IYandexMetrikaIdentityStore>();

        erasure.Should().BeOfType<NoOpProductAnalyticsErasure>();
        identities.Should().BeOfType<NoOpYandexMetrikaIdentityStore>();
    }

    [Fact]
    public void MissingConnectionString_ResolvesNoOpErasure()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:IdentitySalt"] = Salt,
            ["ProductAnalytics:Environment"] = EnvironmentLabel,
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddProductAnalytics(configuration);

        var services = serviceCollection.BuildServiceProvider();
        var erasure = services.GetRequiredService<IProductAnalyticsErasure>();

        erasure.Should().BeOfType<NoOpProductAnalyticsErasure>();
    }

    [Fact]
    public async Task NoOpErasure_ReturnsZero()
    {
        var erasure = new NoOpProductAnalyticsErasure();

        var result = await erasure.PurgeUserAsync("any-user");

        result.Should().Be(0);
    }

    private string GetDistinctId(string userId)
    {
        var pseudonymizer = new UserPseudonymizer(new ProductAnalyticsOptions
        {
            IdentitySalt = Salt,
        });
        return pseudonymizer.ComputeDistinctId(userId);
    }

    private static IServiceProvider BuildServices(string connectionString, int queueCapacity = 100)
    {
        var configValues = new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "Postgres",
            ["ProductAnalytics:ConnectionString"] = connectionString,
            ["ProductAnalytics:IdentitySalt"] = Salt,
            ["ProductAnalytics:Environment"] = EnvironmentLabel,
            ["ProductAnalytics:QueueCapacity"] = queueCapacity.ToString(),
            ["ProductAnalytics:BatchSize"] = "10",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddProductAnalytics(configuration);

        return serviceCollection.BuildServiceProvider();
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

        public void ApplyMigration()
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            var migrationsPath = Path.Combine(FindRepositoryRoot(), "db", "migrations", "analytics");
            foreach (var fileName in new[]
                     {
                         "001_product_analytics_events.sql",
                         "005_yandex_metrika_identities.sql"
                     })
            {
                connection.Execute(File.ReadAllText(Path.Combine(migrationsPath, fileName)));
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
