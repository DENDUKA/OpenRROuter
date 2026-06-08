using Dapper;
using FluentAssertions;
using Npgsql;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Tests.Infrastructure.Persistence;

public sealed class BillingRepositoryPostgresTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task AcquireStalePayments_PersistentLeasePreventsSecondWorker()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;
        database.ApplyMigration();
        database.Truncate();
        var firstRepository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var secondRepository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var now = DateTimeOffset.UtcNow;
        await firstRepository.CreatePaymentAsync(CreatePayment("payment-lease", now.AddHours(-1)));

        var first = await firstRepository.AcquireStalePaymentsAsync(
            "worker-1", TimeSpan.FromMinutes(5), now, 10);
        var second = await secondRepository.AcquireStalePaymentsAsync(
            "worker-2", TimeSpan.FromMinutes(5), now, 10);

        first.Should().ContainSingle(p => p.Id == "payment-lease");
        second.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task AcquireUnprocessedEvents_ConcurrentReplicas_ProcessEventOnce()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;
        database.ApplyMigration();
        database.Truncate();
        var firstRepository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var secondRepository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var now = DateTimeOffset.UtcNow;
        var payment = await firstRepository.CreatePaymentAsync(CreatePayment("payment-recovery", now));
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                insert into billing.payment_events (
                    id, payment_id, provider, provider_event_id, provider_payment_id, event_type,
                    order_id, confirmed_amount, confirmed_currency, reason, raw_payload, signature_ok,
                    processed, attempts, received_at_utc)
                values (
                    'event-recovery', @PaymentId, 'mock', 'provider-event-recovery', null, 'paid',
                    @OrderId, 10, 'USD', null, '{"ignored":true}', true, false, 0, @Now);
                """,
                new { PaymentId = payment.Id, payment.OrderId, Now = now });
        }

        var results = await Task.WhenAll(
            firstRepository.AcquireUnprocessedEventsAsync(10, now),
            secondRepository.AcquireUnprocessedEventsAsync(10, now));

        results.Sum().Should().Be(1);
        (await firstRepository.GetBalanceAsync(payment.UserId)).Amount.Should().Be(10);
        (await firstRepository.ListUnprocessedEventsAsync(10)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConfirmPaymentAtomic_PersistsPaidEventAndExternalTopUp()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;
        database.ApplyMigration();
        database.Truncate();
        var repository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var now = DateTimeOffset.UtcNow;
        var payment = new Payment("payment-1", "user-1", "mock", null, "payment-1", "client-1",
            1000, "RUB", 1000, "RUB", MoneyAmount.Usd(10), 100, "fallback", now,
            PaymentStatus.Created, null, null, null, now, now, null, null, null);
        await repository.CreatePaymentAsync(payment);

        var outcome = await repository.ConfirmPaymentAtomicAsync(new PaymentEventInput(
            "mock", null, "payment-1", "paid", 1000, "RUB", null, "provider-event-1", "{}", true), now);

        outcome.Confirmed.Should().BeTrue();
        (await repository.GetBalanceAsync("user-1")).Amount.Should().Be(10);
        (await repository.ListTopUpsAsync("user-1", 10)).Items.Should().ContainSingle(e => e.Type == LedgerEntryType.ExternalTopUp);
        (await repository.ListUnprocessedEventsAsync(10)).Should().BeEmpty();
    }

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
    public async Task DeleteTag_RemovesTagIdsFromModels()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        database.ApplyMigration();
        var repository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        await repository.UpsertTagAsync(new Tag("fast", "Fast", "#0d6efd"));
        await repository.UpsertTagAsync(new Tag("cheap", "Cheap", "#198754"));
        await repository.UpsertModelAsync(new ModelPrice(
            "gpt-test",
            "GPT Test",
            "router/gpt-test",
            "v1",
            new ModelTokenPrice(MoneyAmount.Usd(10), MoneyAmount.Usd(20), MoneyAmount.Usd(2)),
            Enabled: true,
            TagIds: ["fast", "cheap"]));

        await repository.DeleteTagAsync("fast");

        var model = (await repository.ListAllModelsAsync(10)).Items.Should().ContainSingle().Which;
        model.TagIds.Should().Equal("cheap");
        (await repository.ListTagsAsync()).Should().ContainSingle(t => t.Id == "cheap");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ModelReasoningCapability_RoundTripsThroughRepository()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        database.ApplyMigration();
        database.Truncate();
        var repository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));

        await repository.UpsertModelAsync(new ModelPrice(
            "gpt-reasoning",
            "GPT Reasoning",
            "router/gpt-reasoning",
            "v1",
            new ModelTokenPrice(MoneyAmount.Usd(10), MoneyAmount.Usd(20), MoneyAmount.Usd(2)),
            Enabled: true,
            SupportsReasoning: true,
            AllowedReasoningEfforts: ["low", "medium", "high"],
            DefaultReasoningEffort: "medium"));

        var model = await repository.GetModelAsync("gpt-reasoning");
        model.Should().NotBeNull();
        model!.SupportsReasoning.Should().BeTrue();
        model.AllowedReasoningEfforts.Should().Equal("low", "medium", "high");
        model.DefaultReasoningEffort.Should().Be("medium");
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

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RecordUsageWithTransactionAsync_PersistsActivityUsageAndLedger()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;

        database.ApplyMigration();
        database.Truncate();
        var repository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var now = DateTimeOffset.UtcNow;

        var activity = new ActivityByTokenRecord(
            "tx-req-1", "key-1", "sk-...", "gpt-test", "node-1",
            new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.001m), "succeeded", now, now, null, null);

        var usage = new UsageRecord(
            "tx-req-1", "user-1", "key-1", "gpt-test", "v1", "node-1",
            new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.001m), "succeeded", now, null, null);

        var ledgerEntry = new LedgerEntry(
            "tx-ledger-1", "user-1", LedgerEntryType.UsageCharge,
            MoneyAmount.Usd(-0.001m), "usage:tx-req-1", "charge", now);

        await repository.RecordUsageWithTransactionAsync(activity, usage, ledgerEntry);

        var savedUsage = await repository.GetUsageAsync("tx-req-1");
        savedUsage.Should().NotBeNull();
        savedUsage!.Cost.Amount.Should().Be(0.001m);

        var (activities, _) = await repository.ListActivityByTokenAsync(["key-1"], 10);
        activities.Should().ContainSingle().Which.RequestId.Should().Be("tx-req-1");

        var balance = await repository.GetBalanceAsync("user-1");
        balance.Amount.Should().Be(-0.001m);
    }

    private static Payment CreatePayment(string id, DateTimeOffset createdAt) =>
        new(id, "user-1", "mock", null, id, $"client-{id}",
            10, "USD", 10, "USD", MoneyAmount.Usd(10), 1, "fallback", createdAt,
            PaymentStatus.Created, null, null, null, createdAt, createdAt, null, null, null);

}
