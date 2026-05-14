using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenRussiaRouter.BillingService.Tests.Infrastructure.Persistence;

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
        foreach (var migration in Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "db", "migrations", "billing"), "*.sql")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            connection.Execute(File.ReadAllText(migration));
        }
    }

    public void Truncate()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Execute("truncate billing.billing_anomalies, billing.usage_records, billing.model_prices, billing.ledger_entries restart identity cascade;");
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
