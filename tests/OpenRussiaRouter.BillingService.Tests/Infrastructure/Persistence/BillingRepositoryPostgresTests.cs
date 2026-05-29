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

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task CurrencyMigration_NormalizesLegacyRubValuesAndPreventsNewOnes()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        database.ApplyMigrationsThrough("009_model_description.sql");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;

        await connection.ExecuteAsync(
            """
            insert into billing.ledger_entries
                (id, user_id, type, amount, currency, idempotency_key, reason, created_at_utc)
            values
                ('ledger-rub', 'user-1', 'VirtualTopUp', 100, 'Rub', 'idem-rub', 'legacy top-up', @Now);

            insert into billing.model_prices
                (model_id, display_name, router_model_id, price_version,
                 input_per_million_amount, output_per_million_amount, cache_per_million_amount,
                 currency, enabled)
            values
                ('gpt-rub', 'GPT Rub', 'router/gpt-rub', 'v1', 10, 20, 1, 'Rub', true);

            insert into billing.usage_records
                (request_id, user_id, api_key_id, model_id, price_version, router_node_id,
                 input_tokens, output_tokens, cache_tokens, cost_amount, cost_currency, status, created_at_utc)
            values
                ('req-rub', 'user-1', 'key-1', 'router/gpt-rub', 'v1', 'node-1',
                 10, 20, 0, 0.001, 'Rub', 'succeeded', @Now);
            """,
            new { Now = now });

        database.ApplyMigrationFile("010_enforce_usd_currency.sql");

        var repository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var model = (await repository.ListAllModelsAsync(10)).Items.Should().ContainSingle().Which;
        model.ModelId.Should().Be("gpt-rub");
        model.Price.InputPerMillion.Currency.Should().Be(Currency.Usd);

        var currencies = await connection.QuerySingleAsync<(string Ledger, string Model, string Usage)>(
            """
            select
                (select currency from billing.ledger_entries where id = 'ledger-rub') as Ledger,
                (select currency from billing.model_prices where model_id = 'gpt-rub') as Model,
                (select cost_currency from billing.usage_records where request_id = 'req-rub') as Usage;
            """);
        currencies.Should().Be(("Usd", "Usd", "Usd"));

        var insertRub = async () => await connection.ExecuteAsync(
            """
            insert into billing.model_prices
                (model_id, display_name, router_model_id, price_version,
                 input_per_million_amount, output_per_million_amount, cache_per_million_amount,
                 currency, enabled)
            values
                ('gpt-new-rub', 'GPT New Rub', 'router/gpt-new-rub', 'v1', 10, 20, 1, 'Rub', true);
            """);
        (await insertRub.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

}
