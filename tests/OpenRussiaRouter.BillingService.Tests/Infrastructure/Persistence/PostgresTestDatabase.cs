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
        catch (Exception ex) when (ex is TimeoutException || (ex is ArgumentException arg && arg.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase)))
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
        foreach (var migration in BillingMigrations().Order(StringComparer.OrdinalIgnoreCase))
        {
            connection.Execute(File.ReadAllText(migration));
        }
    }

    public void ApplyMigrationsThrough(string lastMigrationFileName)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        foreach (var migration in BillingMigrations().Order(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Compare(Path.GetFileName(migration), lastMigrationFileName, StringComparison.OrdinalIgnoreCase) > 0)
            {
                break;
            }

            connection.Execute(File.ReadAllText(migration));
        }
    }

    public void ApplyMigrationFile(string migrationFileName)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Execute(File.ReadAllText(Path.Combine(BillingMigrationsDirectory(), migrationFileName)));
    }

    public void Truncate()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Execute("truncate billing.yandex_metrika_offline_conversion_outbox, billing.payment_events, billing.payments, billing.billing_anomalies, billing.usage_records, billing.model_prices, billing.ledger_entries restart identity cascade;");
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

    private static string BillingMigrationsDirectory()
        => Path.Combine(FindRepositoryRoot(), "db", "migrations", "billing");

    private static IEnumerable<string> BillingMigrations()
        => Directory.EnumerateFiles(BillingMigrationsDirectory(), "*.sql");
}
