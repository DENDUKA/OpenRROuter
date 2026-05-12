using Dapper;
using OpenRussiaRouter.BillingService.Application;
using OpenRussiaRouter.BillingService.Domain;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

/// <summary>
/// Dapper/PostgreSQL-backed Billing store.
/// </summary>
public sealed class DapperBillingStore(IBillingDbConnectionFactory connectionFactory) : IBillingStore
{
    /// <inheritdoc />
    public LedgerEntry AddLedgerEntry(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var connection = connectionFactory.CreateConnection();
        connection.Execute(
            """
            insert into billing.ledger_entries
                (id, user_id, type, amount, currency, idempotency_key, reason, created_at_utc)
            values
                (@Id, @UserId, @Type, @Amount, @Currency, @IdempotencyKey, @Reason, @CreatedAtUtc)
            on conflict (idempotency_key) do nothing;
            """,
            new
            {
                entry.Id,
                entry.UserId,
                Type = entry.Type.ToString(),
                entry.Amount.Amount,
                Currency = entry.Amount.Currency.ToString(),
                entry.IdempotencyKey,
                entry.Reason,
                entry.CreatedAtUtc
            });

        return BillingPersistenceMapper.ToDomain(connection.QuerySingle<LedgerEntryEntity>(
            LedgerByIdempotencySql,
            new { entry.IdempotencyKey }));
    }

    /// <inheritdoc />
    public MoneyAmount GetBalance(string userId)
    {
        using var connection = connectionFactory.CreateConnection();
        var amount = connection.ExecuteScalar<decimal>(
            "select coalesce(sum(amount), 0) from billing.ledger_entries where user_id = @UserId;",
            new { UserId = userId });

        return MoneyAmount.Rub(amount);
    }

    /// <inheritdoc />
    public void UpsertModel(ModelPrice model)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var connection = connectionFactory.CreateConnection();
        connection.Execute(
            """
            insert into billing.model_prices
                (model_id, display_name, router_model_id, price_version, input_per_million_amount,
                 output_per_million_amount, cache_per_million_amount, currency, enabled)
            values
                (@ModelId, @DisplayName, @RouterModelId, @PriceVersion, @InputPerMillionAmount,
                 @OutputPerMillionAmount, @CachePerMillionAmount, @Currency, @Enabled)
            on conflict (model_id) do update set
                display_name = excluded.display_name,
                router_model_id = excluded.router_model_id,
                price_version = excluded.price_version,
                input_per_million_amount = excluded.input_per_million_amount,
                output_per_million_amount = excluded.output_per_million_amount,
                cache_per_million_amount = excluded.cache_per_million_amount,
                currency = excluded.currency,
                enabled = excluded.enabled;
            """,
            new
            {
                model.ModelId,
                model.DisplayName,
                model.RouterModelId,
                model.PriceVersion,
                InputPerMillionAmount = model.Price.InputPerMillion.Amount,
                OutputPerMillionAmount = model.Price.OutputPerMillion.Amount,
                CachePerMillionAmount = model.Price.CachePerMillion.Amount,
                Currency = model.Price.InputPerMillion.Currency.ToString(),
                model.Enabled
            });
    }

    /// <inheritdoc />
    public ModelPrice? GetEnabledModel(string modelId)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<ModelPriceEntity>(
            ModelPriceSelectSql + " where model_id = @ModelId and enabled = true;",
            new { ModelId = modelId }) is { } entity
                ? BillingPersistenceMapper.ToDomain(entity)
                : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ModelPrice> ListEnabledModels()
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.Query<ModelPriceEntity>(
                ModelPriceSelectSql + " where enabled = true order by model_id;")
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();
    }

    /// <inheritdoc />
    public UsageRecord AddUsage(UsageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = connectionFactory.CreateConnection();
        connection.Execute(
            """
            insert into billing.usage_records
                (request_id, user_id, api_key_id, model_id, price_version, router_node_id,
                 input_tokens, output_tokens, cache_tokens, cost_amount, cost_currency, status, created_at_utc)
            values
                (@RequestId, @UserId, @ApiKeyId, @ModelId, @PriceVersion, @RouterNodeId,
                 @InputTokens, @OutputTokens, @CacheTokens, @CostAmount, @CostCurrency, @Status, @CreatedAtUtc)
            on conflict (request_id) do nothing;
            """,
            new
            {
                record.RequestId,
                record.UserId,
                record.ApiKeyId,
                record.ModelId,
                record.PriceVersion,
                record.RouterNodeId,
                record.Usage.InputTokens,
                record.Usage.OutputTokens,
                record.Usage.CacheTokens,
                CostAmount = record.Cost.Amount,
                CostCurrency = record.Cost.Currency.ToString(),
                record.Status,
                record.CreatedAtUtc
            });

        return BillingPersistenceMapper.ToDomain(connection.QuerySingle<UsageRecordEntity>(
            UsageByRequestSql,
            new { record.RequestId }));
    }

    /// <inheritdoc />
    public UsageRecord? GetUsage(string requestId)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<UsageRecordEntity>(
            UsageByRequestSql,
            new { RequestId = requestId }) is { } entity
                ? BillingPersistenceMapper.ToDomain(entity)
                : null;
    }

    /// <inheritdoc />
    public void AddAnomaly(BillingAnomaly anomaly)
    {
        ArgumentNullException.ThrowIfNull(anomaly);
        using var connection = connectionFactory.CreateConnection();
        connection.Execute(
            """
            insert into billing.billing_anomalies
                (id, request_id, type, message, created_at_utc)
            values
                (@Id, @RequestId, @Type, @Message, @CreatedAtUtc);
            """,
            anomaly);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<BillingAnomaly> ListAnomalies()
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.Query<BillingAnomalyEntity>(
                """
                select
                    id as Id,
                    request_id as RequestId,
                    type as Type,
                    message as Message,
                    created_at_utc as CreatedAtUtc
                from billing.billing_anomalies
                order by created_at_utc;
                """)
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();
    }

    private const string LedgerByIdempotencySql = """
        select
            id as Id,
            user_id as UserId,
            type as Type,
            amount as Amount,
            currency as Currency,
            idempotency_key as IdempotencyKey,
            reason as Reason,
            created_at_utc as CreatedAtUtc
        from billing.ledger_entries
        where idempotency_key = @IdempotencyKey;
        """;

    private const string ModelPriceSelectSql = """
        select
            model_id as ModelId,
            display_name as DisplayName,
            router_model_id as RouterModelId,
            price_version as PriceVersion,
            input_per_million_amount as InputPerMillionAmount,
            output_per_million_amount as OutputPerMillionAmount,
            cache_per_million_amount as CachePerMillionAmount,
            currency as Currency,
            enabled as Enabled
        from billing.model_prices
        """;

    private const string UsageByRequestSql = """
        select
            request_id as RequestId,
            user_id as UserId,
            api_key_id as ApiKeyId,
            model_id as ModelId,
            price_version as PriceVersion,
            router_node_id as RouterNodeId,
            input_tokens as InputTokens,
            output_tokens as OutputTokens,
            cache_tokens as CacheTokens,
            cost_amount as CostAmount,
            cost_currency as CostCurrency,
            status as Status,
            created_at_utc as CreatedAtUtc
        from billing.usage_records
        where request_id = @RequestId;
        """;
}
