using Dapper;
using FluentAssertions;
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

        await repository.AddLedgerEntryAsync(new LedgerEntry("ledger-1", "user-1", LedgerEntryType.VirtualTopUp, MoneyAmount.Usd(100), "idem-1", "top-up", now));
        await repository.UpsertModelAsync(new ModelPrice(
            "gpt-test",
            "GPT Test",
            "router/gpt-test",
            "v1",
            new ModelTokenPrice(MoneyAmount.Usd(10), MoneyAmount.Usd(20), MoneyAmount.Usd(2)),
            Enabled: true));
        await repository.AddUsageAsync(new UsageRecord(
            "req-1",
            "user-1",
            "key-1",
            "gpt-test",
            "v1",
            "node-1",
            new TokenUsage(10, 20, 0),
            MoneyAmount.Usd(0.001m),
            "succeeded",
            now));
        await repository.AddAnomalyAsync(new BillingAnomaly("anomaly-1", "req-2", "missing_usage", "safe message", now));

        (await repository.GetBalanceAsync("user-1")).Amount.Should().Be(100);
        (await repository.GetEnabledModelAsync("router/gpt-test"))?.ModelId.Should().Be("gpt-test");
        (await repository.GetUsageAsync("req-1"))?.RequestId.Should().Be("req-1");
        (await repository.ListAnomaliesAsync(100, null)).Items.Should().ContainSingle().Which.Id.Should().Be("anomaly-1");
    }

}
