using Dapper;
using Npgsql;
using OpenRussiaRouter.BillingService.Domain;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Tests.Infrastructure.Persistence;

public sealed class DapperBillingStorePostgresTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public void Store_AppliedMigration_PersistsLedgerModelUsageAndAnomaly()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        ApplyMigration(connectionString, "billing");
        using var connection = new NpgsqlConnection(connectionString);
        connection.Execute("truncate billing.billing_anomalies, billing.usage_records, billing.model_prices, billing.ledger_entries restart identity cascade;");
        var store = new DapperBillingStore(new NpgsqlBillingDbConnectionFactory(connectionString));
        var now = DateTimeOffset.UtcNow;

        store.AddLedgerEntry(new LedgerEntry("ledger-1", "user-1", LedgerEntryType.VirtualTopUp, MoneyAmount.Rub(100), "idem-1", "top-up", now));
        store.UpsertModel(new ModelPrice(
            "gpt-test",
            "GPT Test",
            "router/gpt-test",
            "v1",
            new ModelTokenPrice(MoneyAmount.Rub(10), MoneyAmount.Rub(20), MoneyAmount.Rub(2)),
            Enabled: true));
        store.AddUsage(new UsageRecord(
            "req-1",
            "user-1",
            "key-1",
            "gpt-test",
            "v1",
            "node-1",
            new TokenUsage(10, 20, 0),
            MoneyAmount.Rub(0.001m),
            "succeeded",
            now));
        store.AddAnomaly(new BillingAnomaly("anomaly-1", "req-2", "missing_usage", "safe message", now));

        Assert.Equal(100, store.GetBalance("user-1").Amount);
        Assert.Equal("gpt-test", store.GetEnabledModel("gpt-test")?.ModelId);
        Assert.Equal("req-1", store.GetUsage("req-1")?.RequestId);
        Assert.Equal("anomaly-1", Assert.Single(store.ListAnomalies()).Id);
    }

    private static string? GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("ORR_BILLING_POSTGRES_TEST_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ORR_POSTGRES_TEST_CONNECTION");
    }

    private static void ApplyMigration(string connectionString, string service)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Execute(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "db", "migrations", service, "001_initial.sql")));
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
