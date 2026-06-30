using System.Text.Json;
using System.Threading.Channels;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using Testcontainers.PostgreSql;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

public sealed class PostgresProductAnalyticsTests : IAsyncLifetime
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
    public async Task AllFourEventTypes_AreInsertedWithAllowedPropertiesAndHmacDistinctId()
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

        await worker.StartAsync(CancellationToken.None);

        sink.UserVerified("user-1", "email");
        sink.ApiKeyCreated("user-1");
        sink.ApiRequestCompleted("user-1", new ApiRequestAnalytics("gpt-4", 200, 100, 200, 150, "usage-1"));
        sink.PaymentCompleted("user-1", new PaymentAnalytics("yookassa", "RUB", 2500m, "payment-1"));

        queue.Writer.Complete();
        await worker.ExecuteTask!;
        await worker.StopAsync(CancellationToken.None);

        using var connection = new NpgsqlConnection(_database.ConnectionString);
        var rows = (await connection.QueryAsync<AnalyticsEventRow>(
            "select event_name, distinct_id, properties, schema_version, environment from analytics.events order by id"))
            .ToList();

        rows.Should().HaveCount(4);

        var userVerified = rows.Single(r => r.event_name == AnalyticsEvents.UserVerified);
        userVerified.distinct_id.Should().NotContain("user-1");
        using (var props = JsonDocument.Parse(userVerified.properties))
        {
            props.RootElement.GetProperty("signup_method").GetString().Should().Be("email");
            props.RootElement.GetProperty("schema_version").GetString().Should().Be("2");
            props.RootElement.GetProperty("environment").GetString().Should().Be(EnvironmentLabel);
        }

        userVerified.schema_version.Should().Be(2);
        userVerified.environment.Should().Be(EnvironmentLabel);

        var apiKey = rows.Single(r => r.event_name == AnalyticsEvents.ApiKeyCreated);
        apiKey.distinct_id.Should().Be(userVerified.distinct_id);
        using (var props = JsonDocument.Parse(apiKey.properties))
        {
            props.RootElement.EnumerateObject().Should().HaveCount(2);
        }

        var apiRequest = rows.Single(r => r.event_name == AnalyticsEvents.ApiRequestCompleted);
        using (var props = JsonDocument.Parse(apiRequest.properties))
        {
            var root = props.RootElement;
            root.GetProperty("model").GetString().Should().Be("gpt-4");
            root.GetProperty("status_class").GetString().Should().Be("2xx");
            root.GetProperty("input_tokens_bucket").GetString().Should().Be("1-1K");
            root.GetProperty("output_tokens_bucket").GetString().Should().Be("1-1K");
            root.GetProperty("latency_bucket").GetString().Should().Be("100-500ms");
            root.TryGetProperty("email", out _).Should().BeFalse();
            root.TryGetProperty("prompt", out _).Should().BeFalse();
            root.TryGetProperty("token", out _).Should().BeFalse();
            root.TryGetProperty("secret", out _).Should().BeFalse();
        }

        var payment = rows.Single(r => r.event_name == AnalyticsEvents.PaymentCompleted);
        using (var props = JsonDocument.Parse(payment.properties))
        {
            var root = props.RootElement;
            root.GetProperty("provider").GetString().Should().Be("yookassa");
            root.GetProperty("currency").GetString().Should().Be("RUB");
            root.GetProperty("amount_bucket").GetString().Should().Be("1K-10K");
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task DuplicateDedupKey_InsertsOnlyOnce()
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

        await worker.StartAsync(CancellationToken.None);

        sink.ApiRequestCompleted("user-1", new ApiRequestAnalytics("gpt-4", 200, 1, 1, 10, "usage-dup"));
        sink.ApiRequestCompleted("user-1", new ApiRequestAnalytics("gpt-4", 200, 2, 2, 20, "usage-dup"));

        queue.Writer.Complete();
        await worker.ExecuteTask!;
        await worker.StopAsync(CancellationToken.None);

        using var connection = new NpgsqlConnection(_database.ConnectionString);
        var count = await connection.ExecuteScalarAsync<int>(
            "select count(*) from analytics.events where event_name = @eventName",
            new { eventName = AnalyticsEvents.ApiRequestCompleted });

        count.Should().Be(1);
    }

    [Fact]
    public void InvalidConnectionString_DoesNotThrowFromSinkMethods()
    {
        var services = BuildServices("Host=invalid;Port=5432;Database=orr_tests;Username=orr;Password=wrong");
        var sink = services.GetRequiredService<IProductAnalytics>();

        var act = () =>
        {
            sink.UserVerified("user-1", "email");
            sink.ApiKeyCreated("user-1");
            sink.ApiRequestCompleted("user-1", new ApiRequestAnalytics("gpt-4", 200, 1, 1, 10, "usage-1"));
            sink.PaymentCompleted("user-1", new PaymentAnalytics("mock", "USD", 10m, "payment-1"));
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void QueueFull_DoesNotThrowOrBlock()
    {
        var services = BuildServices("Host=invalid;Port=5432;Database=orr_tests;Username=orr;Password=wrong", queueCapacity: 1);
        var sink = services.GetRequiredService<IProductAnalytics>();
        var queue = services.GetRequiredService<ProductAnalyticsEventQueue>();

        var act = () =>
        {
            for (var i = 0; i < 100; i++)
            {
                sink.ApiKeyCreated($"user-{i}");
            }
        };

        act.Should().NotThrow();
        Drain(queue.Reader).Should().BeLessOrEqualTo(1);
    }

    [Fact]
    public void Resolve_EnabledPostgresWithConnectionString_ReturnsPostgresSinkAndWorker()
    {
        var services = BuildServices("Host=localhost;Port=5432;Database=orr;Username=orr;Password=p");

        var analytics = services.GetRequiredService<IProductAnalytics>();
        analytics.Should().BeOfType<PostgresProductAnalytics>();

        var hostedServices = services.GetServices<IHostedService>();
        hostedServices.Should().ContainSingle(s => s is ProductAnalyticsFlushWorker);
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

    private static int Drain(ChannelReader<QueuedAnalyticsEvent> reader)
    {
        var count = 0;
        while (reader.TryRead(out _))
        {
            count++;
        }

        return count;
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
            catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        public void ApplyMigration()
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            var migrationPath = Path.Combine(
                FindRepositoryRoot(),
                "db",
                "migrations",
                "analytics",
                "001_product_analytics_events.sql");
            connection.Execute(File.ReadAllText(migrationPath));
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

    private sealed class AnalyticsEventRow
    {
        public string event_name { get; set; } = string.Empty;
        public string distinct_id { get; set; } = string.Empty;
        public string properties { get; set; } = string.Empty;
        public int schema_version { get; set; }
        public string environment { get; set; } = string.Empty;
    }
}
