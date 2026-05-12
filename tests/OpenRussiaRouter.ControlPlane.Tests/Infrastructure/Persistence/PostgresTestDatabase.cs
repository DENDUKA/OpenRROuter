using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenRussiaRouter.ControlPlane.Tests.Infrastructure.Persistence;

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
        connection.Execute(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "db", "migrations", "control", "001_initial.sql")));
    }

    public void Truncate()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Execute("truncate control.router_leases, control.router_nodes restart identity cascade;");
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
