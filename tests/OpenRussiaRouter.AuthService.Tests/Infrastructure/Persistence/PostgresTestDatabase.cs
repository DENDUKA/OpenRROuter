using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenRussiaRouter.AuthService.Tests.Infrastructure.Persistence;

public sealed class PostgresTestDatabase : IAsyncDisposable
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
        catch (Exception ex) when (ex is TimeoutException || (ex is ArgumentException arg && arg.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
    }

    public void ApplyAuthMigration()
    {
        var migrationsDir = Path.Combine(FindRepositoryRoot(), "db", "migrations", "auth");
        using var connection = new NpgsqlConnection(ConnectionString);
        foreach (var file in Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f))
        {
            connection.Execute(File.ReadAllText(file));
        }
    }

    public void TruncateAuth()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Execute("truncate auth.api_keys, auth.invites, auth.users restart identity cascade;");
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
