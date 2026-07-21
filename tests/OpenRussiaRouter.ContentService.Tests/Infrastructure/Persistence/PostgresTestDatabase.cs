using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenRussiaRouter.ContentService.Tests.Infrastructure.Persistence;

/// <summary>
/// Spins up a throwaway postgres via Testcontainers. Returns <c>null</c> when Docker is
/// unavailable so the suite skips quietly instead of failing on developer machines without it.
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly PostgreSqlContainer container;

    private PostgresTestDatabase(PostgreSqlContainer container)
    {
        this.container = container;
    }

    public string ConnectionString =>
        Environment.GetEnvironmentVariable("ORR_CONTENT_POSTGRES_TEST_CONNECTION")
        ?? container.GetConnectionString();

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
        catch (Exception ex) when (ex is TimeoutException
                                       || (ex is ArgumentException arg
                                           && arg.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    public void ApplyMigrations()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        foreach (var migration in ContentMigrations().Order(StringComparer.OrdinalIgnoreCase))
        {
            connection.Execute(File.ReadAllText(migration));
        }
    }

    public void Truncate()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Execute("truncate content.tool_images, content.tools restart identity cascade;");
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

    private static string ContentMigrationsDirectory()
        => Path.Combine(FindRepositoryRoot(), "db", "migrations", "content");

    private static IEnumerable<string> ContentMigrations()
        => Directory.EnumerateFiles(ContentMigrationsDirectory(), "*.sql");
}
