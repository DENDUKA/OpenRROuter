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
    public async Task AcquireStalePayments_PreservesBonusSnapshot()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;
        database.ApplyMigration();
        database.Truncate();
        var repository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var now = DateTimeOffset.UtcNow;
        var createdAt = now.AddHours(-1);

        var payment = new Payment(
            "payment-bonus", "user-1", "mock", null, "payment-bonus", "client-bonus",
            1000, "RUB", 1000, "RUB", MoneyAmount.Usd(10), 100, "fallback", createdAt,
            PaymentStatus.Created, null, null, null, createdAt, createdAt, null, null, null,
            BonusPercent: 20m);
        await repository.CreatePaymentAsync(payment);

        var acquired = (await repository.AcquireStalePaymentsAsync(
            "worker-1", TimeSpan.FromMinutes(5), now, 10)).Should().ContainSingle().Which;

        acquired.Id.Should().Be("payment-bonus");
        acquired.BonusPercent.Should().Be(20m);
        acquired.BonusUsd.Should().BeNull();
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
        var usage = new UsageRecord(
            "req-1",
            "user-1",
            "key-1",
            "gpt-test",
            "v1",
            "node-1",
            new TokenUsage(10, 20, 0),
            MoneyAmount.Usd(0.001m),
            "succeeded",
            now);
        var activity = new ActivityByTokenRecord(
            "req-1",
            "key-1",
            "sk-or-xxx",
            "gpt-test",
            "node-1",
            new TokenUsage(10, 20, 0),
            MoneyAmount.Usd(0.001m),
            "completed",
            now,
            now);
        await repository.RecordUsageWithTransactionAsync(activity, usage, null);
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
    public async Task ModelPriority_RoundTripsAndUpdatesThroughRepository()
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
            "gpt-priority",
            "GPT Priority",
            "router/gpt-priority",
            "v1",
            new ModelTokenPrice(MoneyAmount.Usd(10), MoneyAmount.Usd(20), MoneyAmount.Usd(2)),
            Enabled: true,
            Priority: 7,
            IsFlagship: true));

        var stored = await repository.GetModelAsync("gpt-priority");
        stored.Should().NotBeNull();
        stored!.Priority.Should().Be(7);
        stored!.IsFlagship.Should().BeTrue();

        // Upsert without explicit Priority falls back to default 0 — verify column is updated on conflict.
        await repository.UpsertModelAsync(new ModelPrice(
            "gpt-priority",
            "GPT Priority",
            "router/gpt-priority",
            "v1",
            new ModelTokenPrice(MoneyAmount.Usd(10), MoneyAmount.Usd(20), MoneyAmount.Usd(2)),
            Enabled: true));

        var refreshed = await repository.GetModelAsync("gpt-priority");
        refreshed.Should().NotBeNull();
        refreshed!.Priority.Should().Be(0);
        refreshed!.IsFlagship.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ListModels_WithProviderJoin_ReturnsModelDisplayName()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        database.ApplyMigration();
        database.Truncate();
        var repository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        await repository.UpsertModelProviderAsync(new ModelProvider("openai", "OpenAI"));
        await repository.UpsertModelAsync(new ModelPrice(
            "gpt-provider",
            "GPT Provider Model",
            "router/gpt-provider",
            "v1",
            new ModelTokenPrice(MoneyAmount.Usd(10), MoneyAmount.Usd(20), MoneyAmount.Usd(2)),
            Enabled: true,
            ProviderId: "openai"));

        var publicModel = (await repository.ListEnabledModelsAsync(10)).Items.Should().ContainSingle().Which;
        var adminModel = (await repository.ListAllModelsAsync(10)).Items.Should().ContainSingle().Which;

        publicModel.DisplayName.Should().Be("GPT Provider Model");
        publicModel.Provider.Should().Be("OpenAI");
        adminModel.DisplayName.Should().Be("GPT Provider Model");
        adminModel.Provider.Should().Be("OpenAI");
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

        // T-028: Seed user balance so the atomic debit can succeed.
        await repository.AddLedgerEntryAsync(new LedgerEntry(
            "tx-topup-1", "user-1", LedgerEntryType.VirtualTopUp,
            MoneyAmount.Usd(1m), "test-topup", "seed", now));

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

        var (activities, _, _) = await repository.ListActivityByTokenAsync(["key-1"], 10);
        activities.Should().ContainSingle().Which.RequestId.Should().Be("tx-req-1");

        var balance = await repository.GetBalanceAsync("user-1");
        balance.Amount.Should().Be(0.999m);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ApiKeySpendLimits_RoundTripAndSummariesUseUsageWindows()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;

        database.ApplyMigration();
        database.Truncate();
        var repository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

        await repository.UpsertApiKeySpendLimitAsync(new ApiKeySpendLimit(
            "user-1", "key-1", MoneyAmount.Usd(25), MoneyAmount.Usd(40), now, now));
        await repository.RecordUsageWithTransactionAsync(
            CreateActivity("req-yesterday", now.AddDays(-1)),
            CreateUsage("req-yesterday", now.AddDays(-1), MoneyAmount.Usd(10)),
            null);
        await repository.RecordUsageWithTransactionAsync(
            CreateActivity("req-today", now),
            CreateUsage("req-today", now, MoneyAmount.Usd(10)),
            null);

        var settings = await repository.GetApiKeySpendLimitAsync("user-1", "key-1");
        var summary = (await repository.ListApiKeyLimitSummariesAsync(
            "user-1",
            ["key-1"],
            new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero))).Should().ContainSingle().Which;

        settings.Should().NotBeNull();
        settings!.DailyLimit.Should().Be(MoneyAmount.Usd(25));
        settings.WeeklyLimit.Should().Be(MoneyAmount.Usd(40));
        summary.DailySpent.Amount.Should().Be(10);
        summary.WeeklySpent.Amount.Should().Be(20);
        summary.DailyRemaining.Amount.Should().Be(15);
        summary.WeeklyRemaining.Amount.Should().Be(20);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ListAllTopUpsAsync_NoFilters_DoesNotThrowParameterTypeResolutionError()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null) return;

        database.ApplyMigration();
        database.Truncate();
        var repository = new BillingRepository(new BillingDbConnectionFactory(database.ConnectionString));
        var now = DateTimeOffset.UtcNow;
        await repository.AddLedgerEntryAsync(new LedgerEntry(
            "ledger-all-1", "user-1", LedgerEntryType.VirtualTopUp, MoneyAmount.Usd(100), "idem-all-1", "top-up", now));
        await repository.AddLedgerEntryAsync(new LedgerEntry(
            "ledger-all-2", "user-2", LedgerEntryType.VirtualTopUp, MoneyAmount.Usd(50), "idem-all-2", "top-up", now));

        var (items, totalCount) = await repository.ListAllTopUpsAsync(limit: 50);

        totalCount.Should().Be(2);
        items.Should().HaveCount(2);

        var byUser = (await repository.ListAllTopUpsAsync(50, userIds: ["user-1"])).Items;
        byUser.Should().ContainSingle().Which.UserId.Should().Be("user-1");

        var byMinAmount = (await repository.ListAllTopUpsAsync(50, minAmountUsd: 75m)).Items;
        byMinAmount.Should().ContainSingle().Which.Amount.Amount.Should().Be(100);

        var byMaxAmount = (await repository.ListAllTopUpsAsync(50, maxAmountUsd: 75m)).Items;
        byMaxAmount.Should().ContainSingle().Which.Amount.Amount.Should().Be(50);
    }

    private static Payment CreatePayment(string id, DateTimeOffset createdAt) =>
        new(id, "user-1", "mock", null, id, $"client-{id}",
            10, "USD", 10, "USD", MoneyAmount.Usd(10), 1, "fallback", createdAt,
            PaymentStatus.Created, null, null, null, createdAt, createdAt, null, null, null);

    private static ActivityByTokenRecord CreateActivity(string requestId, DateTimeOffset createdAt) =>
        new(requestId, "key-1", "sk-or-test", "gpt-test", "node-1",
            new TokenUsage(1, 0, 0), MoneyAmount.Usd(10), "succeeded", createdAt, createdAt);

    private static UsageRecord CreateUsage(string requestId, DateTimeOffset createdAt, MoneyAmount cost) =>
        new(requestId, "user-1", "key-1", "gpt-test", "v1", "node-1",
            new TokenUsage(1, 0, 0), cost, "succeeded", createdAt);

}
