using Dapper;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

public sealed class BillingRepository(IBillingDbConnectionFactory connectionFactory) : IBillingRepository
{

    public (IReadOnlyCollection<LedgerEntry> Items, bool HasMore) ListTopUps(string userId, int limit)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = connection.Query<LedgerEntryEntity>(
                LedgerEntrySelectSql +
                "where user_id = @UserId and type = 'VirtualTopUp'\norder by created_at_utc desc\nlimit @Limit;",
                new { UserId = userId, Limit = limit + 1 })
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    public LedgerEntry AddLedgerEntry(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var connection = connectionFactory.CreateConnection();
        return BillingPersistenceMapper.ToDomain(connection.QueryFirst<LedgerEntryEntity>(
            """
            with ins as (
                insert into billing.ledger_entries
                    (id, user_id, type, amount, currency, idempotency_key, reason, created_at_utc)
                values
                    (@Id, @UserId, @Type, @Amount, @Currency, @IdempotencyKey, @Reason, @CreatedAtUtc)
                on conflict (idempotency_key) do nothing
                returning *
            )
            select id as Id, user_id as UserId, type as Type, amount as Amount, currency as Currency,
                   idempotency_key as IdempotencyKey, reason as Reason, created_at_utc as CreatedAtUtc
            from ins
            union all
            select id as Id, user_id as UserId, type as Type, amount as Amount, currency as Currency,
                   idempotency_key as IdempotencyKey, reason as Reason, created_at_utc as CreatedAtUtc
            from billing.ledger_entries
            where idempotency_key = @IdempotencyKey
              and not exists (select 1 from ins)
            limit 1;
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
            }));
    }

    public MoneyAmount GetBalance(string userId)
    {
        using var connection = connectionFactory.CreateConnection();
        var amount = connection.ExecuteScalar<decimal>(
            "select coalesce(sum(amount), 0) from billing.ledger_entries where user_id = @UserId;",
            new { UserId = userId });

        return MoneyAmount.Rub(amount);
    }

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

    public ModelPrice? GetEnabledModel(string modelId)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<ModelPriceEntity>(
            ModelPriceSelectSql + "where model_id = @ModelId and enabled = true;",
            new { ModelId = modelId }) is { } entity
                ? BillingPersistenceMapper.ToDomain(entity)
                : null;
    }

    public (IReadOnlyCollection<ModelPrice> Items, bool HasMore) ListEnabledModels(int limit, string? afterModelId = null)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterModelId != null)
        {
            afterCondition = "and model_id > @AfterModelId\n";
            parameters.Add("AfterModelId", afterModelId);
        }

        var items = connection.Query<ModelPriceEntity>(
                ModelPriceSelectSql + "where enabled = true\n" + afterCondition + "order by model_id\nlimit @Limit;",
                parameters)
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    public (IReadOnlyCollection<ModelPrice> Items, bool HasMore) ListAllModels(int limit, string? afterModelId = null)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterModelId != null)
        {
            afterCondition = "where model_id > @AfterModelId\n";
            parameters.Add("AfterModelId", afterModelId);
        }

        var items = connection.Query<ModelPriceEntity>(
                ModelPriceSelectSql + afterCondition + "order by model_id\nlimit @Limit;",
                parameters)
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    public UsageRecord AddUsage(UsageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = connectionFactory.CreateConnection();
        return BillingPersistenceMapper.ToDomain(connection.QueryFirst<UsageRecordEntity>(
            """
            with ins as (
                insert into billing.usage_records
                    (request_id, user_id, api_key_id, model_id, price_version, router_node_id,
                     input_tokens, output_tokens, cache_tokens, cost_amount, cost_currency, status, created_at_utc)
                values
                    (@RequestId, @UserId, @ApiKeyId, @ModelId, @PriceVersion, @RouterNodeId,
                     @InputTokens, @OutputTokens, @CacheTokens, @CostAmount, @CostCurrency, @Status, @CreatedAtUtc)
                on conflict (request_id) do nothing
                returning *
            )
            select request_id as RequestId, user_id as UserId, api_key_id as ApiKeyId, model_id as ModelId,
                   price_version as PriceVersion, router_node_id as RouterNodeId,
                   input_tokens as InputTokens, output_tokens as OutputTokens, cache_tokens as CacheTokens,
                   cost_amount as CostAmount, cost_currency as CostCurrency, status as Status,
                   created_at_utc as CreatedAtUtc
            from ins
            union all
            select request_id as RequestId, user_id as UserId, api_key_id as ApiKeyId, model_id as ModelId,
                   price_version as PriceVersion, router_node_id as RouterNodeId,
                   input_tokens as InputTokens, output_tokens as OutputTokens, cache_tokens as CacheTokens,
                   cost_amount as CostAmount, cost_currency as CostCurrency, status as Status,
                   created_at_utc as CreatedAtUtc
            from billing.usage_records
            where request_id = @RequestId
              and not exists (select 1 from ins)
            limit 1;
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
            }));
    }

    public UsageRecord? GetUsage(string requestId)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<UsageRecordEntity>(
            UsageByRequestSql,
            new { RequestId = requestId }) is { } entity
                ? BillingPersistenceMapper.ToDomain(entity)
                : null;
    }

    public (IReadOnlyCollection<UsageRecord> Items, bool HasMore) ListUsage(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? userId = null)
    {
        using var connection = connectionFactory.CreateConnection();
        var conditions = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        if (afterCreatedAtUtc.HasValue)
        {
            conditions.Add("created_at_utc > @AfterCreatedAtUtc");
            parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
        }
        if (userId != null)
        {
            conditions.Add("user_id = @UserId");
            parameters.Add("UserId", userId);
        }

        var whereClause = conditions.Count > 0
            ? "where " + string.Join(" and ", conditions) + "\n"
            : "";
        var items = connection.Query<UsageRecordEntity>(
                UsageSelectSql + whereClause + "order by created_at_utc\nlimit @Limit;",
                parameters)
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    public ActivityByTokenRecord AddActivityByToken(ActivityByTokenRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = connectionFactory.CreateConnection();
        return BillingPersistenceMapper.ToDomain(connection.QueryFirst<ActivityByTokenEntity>(
            """
            with ins as (
                insert into billing.activity_by_token
                    (request_id, api_key_id, api_key_prefix, model_id, router_node_id,
                     input_tokens, output_tokens, cache_tokens, status, created_at_utc, completed_at_utc)
                values
                    (@RequestId, @ApiKeyId, @ApiKeyPrefix, @ModelId, @RouterNodeId,
                     @InputTokens, @OutputTokens, @CacheTokens, @Status, @CreatedAtUtc, @CompletedAtUtc)
                on conflict (request_id) do nothing
                returning *
            )
            select request_id as RequestId, api_key_id as ApiKeyId, api_key_prefix as ApiKeyPrefix,
                   model_id as ModelId, router_node_id as RouterNodeId,
                   input_tokens as InputTokens, output_tokens as OutputTokens, cache_tokens as CacheTokens,
                   status as Status, created_at_utc as CreatedAtUtc, completed_at_utc as CompletedAtUtc
            from ins
            union all
            select request_id as RequestId, api_key_id as ApiKeyId, api_key_prefix as ApiKeyPrefix,
                   model_id as ModelId, router_node_id as RouterNodeId,
                   input_tokens as InputTokens, output_tokens as OutputTokens, cache_tokens as CacheTokens,
                   status as Status, created_at_utc as CreatedAtUtc, completed_at_utc as CompletedAtUtc
            from billing.activity_by_token
            where request_id = @RequestId
              and not exists (select 1 from ins)
            limit 1;
            """,
            new
            {
                record.RequestId,
                record.ApiKeyId,
                record.ApiKeyPrefix,
                record.ModelId,
                record.RouterNodeId,
                record.Usage.InputTokens,
                record.Usage.OutputTokens,
                record.Usage.CacheTokens,
                record.Status,
                record.CreatedAtUtc,
                record.CompletedAtUtc
            }));
    }

    public (IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore) ListActivityByToken(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? afterCreatedAtUtc = null)
    {
        if (apiKeyIds.Count == 0)
        {
            return (Array.Empty<ActivityByTokenRecord>(), false);
        }

        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("ApiKeyIds", apiKeyIds.ToArray());
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterCreatedAtUtc.HasValue)
        {
            afterCondition = "  and created_at_utc > @AfterCreatedAtUtc\n";
            parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
        }

        var items = connection.Query<ActivityByTokenEntity>(
                ActivityByTokenSelectSql +
                "where api_key_id = any(@ApiKeyIds)\n" + afterCondition +
                "order by created_at_utc desc\nlimit @Limit;",
                parameters)
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

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

    public (IReadOnlyCollection<BillingAnomaly> Items, bool HasMore) ListAnomalies(int limit, DateTimeOffset? afterCreatedAtUtc = null)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterCreatedAtUtc.HasValue)
        {
            afterCondition = "where created_at_utc > @AfterCreatedAtUtc\n";
            parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
        }

        var items = connection.Query<BillingAnomalyEntity>(
                """
                select
                    id as Id,
                    request_id as RequestId,
                    type as Type,
                    message as Message,
                    created_at_utc as CreatedAtUtc
                from billing.billing_anomalies

                """ + afterCondition + "order by created_at_utc\nlimit @Limit;",
                parameters)
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    private const string LedgerEntrySelectSql = """
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

    private const string UsageSelectSql = """
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

        """;

    private const string UsageByRequestSql = UsageSelectSql + """
        where request_id = @RequestId;
        """;

    private const string ActivityByTokenSelectSql = """
        select
            request_id as RequestId,
            api_key_id as ApiKeyId,
            api_key_prefix as ApiKeyPrefix,
            model_id as ModelId,
            router_node_id as RouterNodeId,
            input_tokens as InputTokens,
            output_tokens as OutputTokens,
            cache_tokens as CacheTokens,
            status as Status,
            created_at_utc as CreatedAtUtc,
            completed_at_utc as CompletedAtUtc
        from billing.activity_by_token

        """;
}
