using Dapper;
using Npgsql;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;

namespace OpenRussiaRouter.BillingService.Tests.Infrastructure.Persistence;

public sealed class BillingRepositoryPostgresTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Store_AppliedMigration_PersistsLedgerModelUsageAndAnomaly()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        var connectionString = database.ConnectionString;
        database.ApplyMigration();
        using var connection = new NpgsqlConnection(connectionString);
        database.Truncate();
        var repository = new BillingRepository(new BillingDbConnectionFactory(connectionString));
        var now = DateTimeOffset.UtcNow;

        repository.AddLedgerEntry(new LedgerEntry("ledger-1", "user-1", LedgerEntryType.VirtualTopUp, MoneyAmount.Rub(100), "idem-1", "top-up", now));
        repository.UpsertModel(new ModelPrice(
            "gpt-test",
            "GPT Test",
            "router/gpt-test",
            "v1",
            new ModelTokenPrice(MoneyAmount.Rub(10), MoneyAmount.Rub(20), MoneyAmount.Rub(2)),
            Enabled: true));
        repository.AddUsage(new UsageRecord(
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
        repository.AddAnomaly(new BillingAnomaly("anomaly-1", "req-2", "missing_usage", "safe message", now));

        Assert.Equal(100, repository.GetBalance("user-1").Amount);
        Assert.Equal("gpt-test", repository.GetEnabledModel("gpt-test")?.ModelId);
        Assert.Equal("req-1", repository.GetUsage("req-1")?.RequestId);
        Assert.Equal("anomaly-1", Assert.Single(repository.ListAnomalies(100, null).Items).Id);
    }

}
