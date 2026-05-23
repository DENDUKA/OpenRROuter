using Dapper;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

public sealed class BillingRepository(IBillingDbConnectionFactory connectionFactory) : IBillingRepository
{
    public async Task<(IReadOnlyCollection<LedgerEntry> Items, bool HasMore)> ListTopUpsAsync(string userId, int limit, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<LedgerEntryEntity>(
                LedgerEntrySelectSql +
                """
                where user_id = @UserId and type = 'VirtualTopUp'
                order by created_at_utc desc
                limit @Limit;
                """,
                new { UserId = userId, Limit = limit + 1 }).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<LedgerEntry> AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var connection = connectionFactory.CreateConnection();
        return BillingPersistenceMapper.ToDomain(await connection.QueryFirstAsync<LedgerEntryEntity>(
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
            }).ConfigureAwait(false));
    }

    public async Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var amount = await connection.ExecuteScalarAsync<decimal>(
            "select coalesce(sum(amount), 0) from billing.ledger_entries where user_id = @UserId;",
            new { UserId = userId }).ConfigureAwait(false);
        return MoneyAmount.Rub(amount);
    }

    public async Task UpsertModelAsync(ModelPrice model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
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
            }).ConfigureAwait(false);
    }

    public async Task<ModelPrice?> GetEnabledModelAsync(string modelId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ModelPriceEntity>(
            ModelPriceSelectSql + "where model_id = @ModelId and enabled = true;",
            new { ModelId = modelId }).ConfigureAwait(false) is { } entity
                ? BillingPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListEnabledModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default)
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

        var items = (await connection.QueryAsync<ModelPriceEntity>(
                ModelPriceSelectSql + "where enabled = true\n" + afterCondition + "order by model_id\nlimit @Limit;",
                parameters).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListAllModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default)
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

        var items = (await connection.QueryAsync<ModelPriceEntity>(
                ModelPriceSelectSql + afterCondition + "order by model_id\nlimit @Limit;",
                parameters).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<UsageRecord> AddUsageAsync(UsageRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = connectionFactory.CreateConnection();
        return BillingPersistenceMapper.ToDomain(await connection.QueryFirstAsync<UsageRecordEntity>(
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
            }).ConfigureAwait(false));
    }

    public async Task<UsageRecord?> GetUsageAsync(string requestId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<UsageRecordEntity>(
            UsageByRequestSql,
            new { RequestId = requestId }).ConfigureAwait(false) is { } entity
                ? BillingPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<(IReadOnlyCollection<UsageRecord> Items, bool HasMore)> ListUsageAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? userId = null, CancellationToken ct = default)
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
        var items = (await connection.QueryAsync<UsageRecordEntity>(
                UsageSelectSql + whereClause + "order by created_at_utc\nlimit @Limit;",
                parameters).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<ActivityByTokenRecord> AddActivityByTokenAsync(ActivityByTokenRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = connectionFactory.CreateConnection();
        return BillingPersistenceMapper.ToDomain(await connection.QueryFirstAsync<ActivityByTokenEntity>(
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
                   null::numeric as CostAmount,
                   status as Status, created_at_utc as CreatedAtUtc, completed_at_utc as CompletedAtUtc
            from ins
            union all
            select request_id as RequestId, api_key_id as ApiKeyId, api_key_prefix as ApiKeyPrefix,
                   model_id as ModelId, router_node_id as RouterNodeId,
                   input_tokens as InputTokens, output_tokens as OutputTokens, cache_tokens as CacheTokens,
                   null::numeric as CostAmount,
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
            }).ConfigureAwait(false));
    }

    public async Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? afterCreatedAtUtc = null,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return (Array.Empty<ActivityByTokenRecord>(), false);

        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("ApiKeyIds", apiKeyIds.ToArray());
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterCreatedAtUtc.HasValue)
        {
            afterCondition = "  and a.created_at_utc > @AfterCreatedAtUtc\n";
            parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
        }

        var items = (await connection.QueryAsync<ActivityByTokenEntity>(
                ActivityByTokenSelectSql +
                "where a.api_key_id = any(@ApiKeyIds)\n" + afterCondition +
                "order by a.created_at_utc desc\nlimit @Limit;",
                parameters).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task AddAnomalyAsync(BillingAnomaly anomaly, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(anomaly);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            insert into billing.billing_anomalies
                (id, request_id, type, message, created_at_utc)
            values
                (@Id, @RequestId, @Type, @Message, @CreatedAtUtc);
            """,
            anomaly).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyCollection<BillingAnomaly> Items, bool HasMore)> ListAnomaliesAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, CancellationToken ct = default)
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

        var items = (await connection.QueryAsync<BillingAnomalyEntity>(
                """
                select
                    id as Id,
                    request_id as RequestId,
                    type as Type,
                    message as Message,
                    created_at_utc as CreatedAtUtc
                from billing.billing_anomalies

                """ + afterCondition + "order by created_at_utc\nlimit @Limit;",
                parameters).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
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
            a.request_id as RequestId,
            a.api_key_id as ApiKeyId,
            a.api_key_prefix as ApiKeyPrefix,
            a.model_id as ModelId,
            a.router_node_id as RouterNodeId,
            a.input_tokens as InputTokens,
            a.output_tokens as OutputTokens,
            a.cache_tokens as CacheTokens,
            coalesce(u.cost_amount, 0) as CostAmount,
            a.status as Status,
            a.created_at_utc as CreatedAtUtc,
            a.completed_at_utc as CompletedAtUtc
        from billing.activity_by_token a
        left join billing.usage_records u on u.request_id = a.request_id

        """;
}
