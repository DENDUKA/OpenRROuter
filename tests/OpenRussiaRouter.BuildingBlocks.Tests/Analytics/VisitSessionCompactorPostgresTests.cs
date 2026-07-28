using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using Testcontainers.PostgreSql;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

/// <summary>
/// Compactor tests against a real PostgreSQL: the interesting parts (advisory lock,
/// <c>on conflict</c> merge, keyset paging, delete scope) live in SQL and cannot be covered
/// in memory. Skipped silently when Docker is unavailable, like the other Postgres tests here.
/// </summary>
public sealed class VisitSessionCompactorPostgresTests : IAsyncLifetime
{
    // Must match VisitSessionCompactor's literals; duplicated on purpose so that changing
    // them there without thinking makes this test fail.
    private const int LockNamespace = 0x004F_5252;
    private const int LockId = 0x0056_5343;

    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SafetyLag = TimeSpan.FromMinutes(5);

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
    public async Task ClosedSession_IsCollapsedAndItsVisitsDeleted()
    {
        if (_database is null)
        {
            return;
        }

        await _database.ResetAsync();

        // One sitting three hours ago: two page views one minute apart.
        await InsertVisitAsync("visitor-a", Now.AddHours(-3), "/", utmSource: "google");
        await InsertVisitAsync("visitor-a", Now.AddHours(-3).AddMinutes(1), "/models", distinctId: "user-1", isAuthenticated: true);

        await using var dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        var compactor = CreateCompactor(dataSource);

        var result = await compactor.CompactOnceAsync(Now, after: null, CancellationToken.None);

        result.Sessions.Should().Be(1);
        result.DeletedVisits.Should().Be(2);
        result.LastVisitorHash.Should().Be("visitor-a");

        (await CountVisitsAsync()).Should().Be(0);

        var session = await ReadSingleSessionAsync();
        session.visitor_hash.Should().Be("visitor-a");
        session.page_view_count.Should().Be(2);
        session.duration_seconds.Should().Be(60);
        session.entry_path.Should().Be("/");
        session.exit_path.Should().Be("/models");
        session.utm_source.Should().Be("google");
        session.distinct_id.Should().Be("user-1");
        session.is_authenticated.Should().BeTrue();

        var pages = ParsePages(session.pages);
        pages.Should().HaveCount(2);
        pages[0].GetProperty("p").GetString().Should().Be("/");
        pages[1].GetProperty("p").GetString().Should().Be("/models");
        pages[0].GetProperty("t").GetString().Should().EndWith("Z");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task LiveSession_IsLeftUntouched()
    {
        if (_database is null)
        {
            return;
        }

        await _database.ResetAsync();

        // Inside the cutoff window (gap + safety lag): still allowed to grow.
        await InsertVisitAsync("visitor-live", Now.AddMinutes(-10), "/");
        await InsertVisitAsync("visitor-live", Now.AddMinutes(-2), "/pricing");

        await using var dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        var result = await CreateCompactor(dataSource).CompactOnceAsync(Now, after: null, CancellationToken.None);

        result.Should().Be(CompactionResult.Empty);
        (await CountVisitsAsync()).Should().Be(2);
        (await CountSessionsAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task VisitorWithClosedAndOpenSession_CollapsesOnlyTheClosedOne()
    {
        if (_database is null)
        {
            return;
        }

        await _database.ResetAsync();

        await InsertVisitAsync("visitor-mix", Now.AddHours(-4), "/");
        await InsertVisitAsync("visitor-mix", Now.AddHours(-4).AddMinutes(2), "/docs");
        await InsertVisitAsync("visitor-mix", Now.AddMinutes(-6), "/models");
        await InsertVisitAsync("visitor-mix", Now.AddMinutes(-1), "/signup");

        await using var dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        var result = await CreateCompactor(dataSource).CompactOnceAsync(Now, after: null, CancellationToken.None);

        result.Sessions.Should().Be(1);
        result.DeletedVisits.Should().Be(2);

        var remaining = await ReadVisitPathsAsync();
        remaining.Should().Equal("/models", "/signup");

        var session = await ReadSingleSessionAsync();
        session.page_view_count.Should().Be(2);
        session.exit_path.Should().Be("/docs");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task SecondPass_IsIdempotent()
    {
        if (_database is null)
        {
            return;
        }

        await _database.ResetAsync();

        await InsertVisitAsync("visitor-a", Now.AddHours(-3), "/");
        await InsertVisitAsync("visitor-a", Now.AddHours(-3).AddMinutes(1), "/models");

        await using var dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        var compactor = CreateCompactor(dataSource);

        await compactor.CompactOnceAsync(Now, after: null, CancellationToken.None);
        var second = await compactor.CompactOnceAsync(Now, after: null, CancellationToken.None);

        second.Should().Be(CompactionResult.Empty);
        (await CountSessionsAsync()).Should().Be(1);
        (await CountVisitsAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task LateFlushedVisit_IsMergedIntoTheExistingSession()
    {
        if (_database is null)
        {
            return;
        }

        await _database.ResetAsync();

        var start = Now.AddHours(-3);
        await InsertVisitAsync("visitor-a", start, "/");
        await InsertVisitAsync("visitor-a", start.AddMinutes(1), "/models");

        await using var dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        var compactor = CreateCompactor(dataSource);
        await compactor.CompactOnceAsync(Now, after: null, CancellationToken.None);

        // Flushed long after the session was written, stamped at the session start.
        await InsertVisitAsync("visitor-a", start, "/late", distinctId: "user-9", isAuthenticated: true);

        var result = await compactor.CompactOnceAsync(Now, after: null, CancellationToken.None);

        result.Sessions.Should().Be(1);
        result.DeletedVisits.Should().Be(1);

        (await CountSessionsAsync()).Should().Be(1);
        (await CountVisitsAsync()).Should().Be(0);

        var session = await ReadSingleSessionAsync();
        session.page_view_count.Should().Be(3);
        session.distinct_id.Should().Be("user-9");
        session.is_authenticated.Should().BeTrue();
        ParsePages(session.pages).Should().HaveCount(3);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task KeysetPaging_AdvancesThroughVisitors()
    {
        if (_database is null)
        {
            return;
        }

        await _database.ResetAsync();

        await InsertVisitAsync("visitor-a", Now.AddHours(-3), "/a");
        await InsertVisitAsync("visitor-b", Now.AddHours(-3), "/b");

        await using var dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        var compactor = CreateCompactor(dataSource, visitorBatchSize: 1);

        var first = await compactor.CompactOnceAsync(Now, after: null, CancellationToken.None);
        first.Sessions.Should().Be(1);
        first.LastVisitorHash.Should().Be("visitor-a");

        var second = await compactor.CompactOnceAsync(Now, first.LastVisitorHash, CancellationToken.None);
        second.Sessions.Should().Be(1);
        second.LastVisitorHash.Should().Be("visitor-b");

        var third = await compactor.CompactOnceAsync(Now, second.LastVisitorHash, CancellationToken.None);
        third.Should().Be(CompactionResult.Empty);

        (await CountSessionsAsync()).Should().Be(2);
        (await CountVisitsAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task AdvisoryLockHeldElsewhere_DoesNothing()
    {
        if (_database is null)
        {
            return;
        }

        await _database.ResetAsync();

        await InsertVisitAsync("visitor-a", Now.AddHours(-3), "/");

        await using var holder = new NpgsqlConnection(_database.ConnectionString);
        await holder.OpenAsync();
        await using var holderTransaction = await holder.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("select pg_try_advisory_xact_lock(@ns, @id);", holder, holderTransaction))
        {
            lockCommand.Parameters.AddWithValue("ns", LockNamespace);
            lockCommand.Parameters.AddWithValue("id", LockId);
            (await lockCommand.ExecuteScalarAsync()).Should().Be(true);
        }

        await using var dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        var result = await CreateCompactor(dataSource).CompactOnceAsync(Now, after: null, CancellationToken.None);

        result.Should().Be(CompactionResult.Empty);
        (await CountVisitsAsync()).Should().Be(1);
        (await CountSessionsAsync()).Should().Be(0);

        await holderTransaction.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task MissingVisitSessionsTable_WarnsOnceAndReturnsEmpty()
    {
        if (_database is null)
        {
            return;
        }

        await _database.ResetAsync();
        await _database.DropVisitSessionsAsync();

        await InsertVisitAsync("visitor-a", Now.AddHours(-3), "/");

        var logger = new CollectingLogger();
        await using var dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        var compactor = new VisitSessionCompactor(
            dataSource,
            new VisitSessionCompactionSettings(SessionGap, SafetyLag, VisitorBatchSize: 500, MaxPagesPerSession: 200),
            logger);

        var first = await compactor.CompactOnceAsync(Now, after: null, CancellationToken.None);
        var second = await compactor.CompactOnceAsync(Now, after: null, CancellationToken.None);

        first.Should().Be(CompactionResult.Empty);
        second.Should().Be(CompactionResult.Empty);
        logger.Warnings.Should().HaveCount(1);
        (await CountVisitsAsync()).Should().Be(1);
    }

    private VisitSessionCompactor CreateCompactor(
        NpgsqlDataSource dataSource,
        int visitorBatchSize = 500,
        int maxPagesPerSession = 200) =>
        new(
            dataSource,
            new VisitSessionCompactionSettings(SessionGap, SafetyLag, visitorBatchSize, maxPagesPerSession),
            NullLogger<VisitSessionCompactor>.Instance);

    private async Task InsertVisitAsync(
        string visitorHash,
        DateTimeOffset occurredAt,
        string path,
        string? distinctId = null,
        bool isAuthenticated = false,
        string? utmSource = null,
        string deviceClass = "desktop")
    {
        await using var connection = new NpgsqlConnection(_database!.ConnectionString);
        await connection.ExecuteAsync(
            """
            insert into analytics.site_visits (
                visitor_hash, distinct_id, occurred_at_utc, path, referrer_host, referrer_kind,
                utm_source, utm_medium, utm_campaign, device_class, is_authenticated, environment)
            values (
                @visitorHash, @distinctId, @occurredAt, @path, null, 'direct',
                @utmSource, null, null, @deviceClass, @isAuthenticated, 'test');
            """,
            new { visitorHash, distinctId, occurredAt, path, utmSource, deviceClass, isAuthenticated });
    }

    private async Task<int> CountVisitsAsync()
    {
        await using var connection = new NpgsqlConnection(_database!.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("select count(*) from analytics.site_visits;");
    }

    private async Task<int> CountSessionsAsync()
    {
        await using var connection = new NpgsqlConnection(_database!.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("select count(*) from analytics.visit_sessions;");
    }

    private async Task<List<string>> ReadVisitPathsAsync()
    {
        await using var connection = new NpgsqlConnection(_database!.ConnectionString);
        var paths = await connection.QueryAsync<string>(
            "select path from analytics.site_visits order by occurred_at_utc, id;");
        return paths.ToList();
    }

    private async Task<VisitSessionSqlRow> ReadSingleSessionAsync()
    {
        await using var connection = new NpgsqlConnection(_database!.ConnectionString);
        var rows = await connection.QueryAsync<VisitSessionSqlRow>(
            """
            select visitor_hash, distinct_id, duration_seconds, page_view_count, entry_path,
                   exit_path, utm_source, is_authenticated, pages::text as pages
            from analytics.visit_sessions
            order by started_at_utc;
            """);
        return rows.Single();
    }

    private static List<JsonElement> ParsePages(string pages)
    {
        using var document = JsonDocument.Parse(pages);
        return document.RootElement.EnumerateArray().Select(static e => e.Clone()).ToList();
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

        /// <summary>
        /// Re-applies the analytics migrations (all of them are <c>if not exists</c>) and
        /// empties both tables, so each test starts from a known state on the shared container.
        /// </summary>
        public async Task ResetAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            foreach (var migration in new[]
                     {
                         "001_product_analytics_events.sql",
                         "002_site_visits.sql",
                         "003_visit_sessions.sql",
                     })
            {
                var path = Path.Combine(FindRepositoryRoot(), "db", "migrations", "analytics", migration);
                await connection.ExecuteAsync(await File.ReadAllTextAsync(path));
            }

            await connection.ExecuteAsync("truncate analytics.site_visits, analytics.visit_sessions;");
        }

        public async Task DropVisitSessionsAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.ExecuteAsync("drop table if exists analytics.visit_sessions;");
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

    private sealed class CollectingLogger : ILogger<VisitSessionCompactor>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter is null ? string.Empty : formatter(state, exception));
            }
        }
    }

    private sealed class VisitSessionSqlRow
    {
        public string visitor_hash { get; set; } = string.Empty;
        public string? distinct_id { get; set; }
        public int duration_seconds { get; set; }
        public int page_view_count { get; set; }
        public string entry_path { get; set; } = string.Empty;
        public string exit_path { get; set; } = string.Empty;
        public string? utm_source { get; set; }
        public bool is_authenticated { get; set; }
        public string pages { get; set; } = string.Empty;
    }
}
