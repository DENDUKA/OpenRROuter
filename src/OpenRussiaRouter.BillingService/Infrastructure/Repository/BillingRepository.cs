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
        var items = (await connection.QueryAsync<LedgerEntryEntity>(new CommandDefinition(
                LedgerEntrySelectSql +
                """
                where user_id = @UserId and type = 'VirtualTopUp'
                order by created_at_utc desc
                limit @Limit;
                """,
                new { UserId = userId, Limit = limit + 1 },
                cancellationToken: ct)).ConfigureAwait(false))
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
        return BillingPersistenceMapper.ToDomain(await connection.QueryFirstAsync<LedgerEntryEntity>(new CommandDefinition(
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
            },
            cancellationToken: ct)).ConfigureAwait(false));
    }

    public async Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var amount = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "select coalesce(sum(amount), 0) from billing.ledger_entries where user_id = @UserId;",
            new { UserId = userId },
            cancellationToken: ct)).ConfigureAwait(false);
        return MoneyAmount.Rub(amount);
    }

    public async Task UpsertModelAsync(ModelPrice model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.model_prices (
                model_id, display_name, router_model_id, price_version,
                input_per_million_amount, output_per_million_amount, cache_per_million_amount,
                currency, enabled,
                description, context_length, modalities, description_source
            )
            values (
                @ModelId, @DisplayName, @RouterModelId, @PriceVersion,
                @InputPerMillionAmount, @OutputPerMillionAmount, @CachePerMillionAmount,
                @Currency, @Enabled,
                @Description, @ContextLength, @Modalities, @DescriptionSource
            )
            on conflict (model_id) do update set
                display_name = excluded.display_name,
                router_model_id = excluded.router_model_id,
                price_version = excluded.price_version,
                input_per_million_amount = excluded.input_per_million_amount,
                output_per_million_amount = excluded.output_per_million_amount,
                cache_per_million_amount = excluded.cache_per_million_amount,
                currency = excluded.currency,
                enabled = excluded.enabled,
                description = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.description
                    ELSE COALESCE(excluded.description, billing.model_prices.description)
                END,
                context_length = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.context_length
                    ELSE COALESCE(excluded.context_length, billing.model_prices.context_length)
                END,
                modalities = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.modalities
                    ELSE COALESCE(excluded.modalities, billing.model_prices.modalities)
                END,
                description_source = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.description_source
                    ELSE billing.model_prices.description_source
                END
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
                model.Enabled,
                model.Description,
                model.ContextLength,
                model.Modalities,
                model.DescriptionSource
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<ModelPrice?> GetEnabledModelAsync(string modelId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ModelPriceEntity>(new CommandDefinition(
            ModelPriceSelectSql + "where router_model_id = @RouterModelId and enabled = true;",
            new { RouterModelId = modelId },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
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

        var items = (await connection.QueryAsync<ModelPriceEntity>(new CommandDefinition(
                ModelPriceSelectSql + "where enabled = true\n" + afterCondition + "order by model_id\nlimit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
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

        var items = (await connection.QueryAsync<ModelPriceEntity>(new CommandDefinition(
                ModelPriceSelectSql + afterCondition + "order by model_id\nlimit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
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
        return BillingPersistenceMapper.ToDomain(await connection.QueryFirstAsync<UsageRecordEntity>(new CommandDefinition(
            """
            with ins as (
                insert into billing.usage_records
                    (request_id, user_id, api_key_id, model_id, price_version, router_node_id,
                     input_tokens, output_tokens, cache_tokens, cost_amount, cost_currency, status, created_at_utc,
                     provider_name, provider_account)
                values
                    (@RequestId, @UserId, @ApiKeyId, @ModelId, @PriceVersion, @RouterNodeId,
                     @InputTokens, @OutputTokens, @CacheTokens, @CostAmount, @CostCurrency, @Status, @CreatedAtUtc,
                     @ProviderName, @ProviderAccount)
                on conflict (request_id) do nothing
                returning *
            )
            select request_id as RequestId, user_id as UserId, api_key_id as ApiKeyId, model_id as ModelId,
                   price_version as PriceVersion, router_node_id as RouterNodeId,
                   input_tokens as InputTokens, output_tokens as OutputTokens, cache_tokens as CacheTokens,
                   cost_amount as CostAmount, cost_currency as CostCurrency, status as Status,
                   created_at_utc as CreatedAtUtc, provider_name as ProviderName, provider_account as ProviderAccount
            from ins
            union all
            select request_id as RequestId, user_id as UserId, api_key_id as ApiKeyId, model_id as ModelId,
                   price_version as PriceVersion, router_node_id as RouterNodeId,
                   input_tokens as InputTokens, output_tokens as OutputTokens, cache_tokens as CacheTokens,
                   cost_amount as CostAmount, cost_currency as CostCurrency, status as Status,
                   created_at_utc as CreatedAtUtc, provider_name as ProviderName, provider_account as ProviderAccount
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
                record.CreatedAtUtc,
                ProviderName = record.ProviderName ?? (object)DBNull.Value,
                ProviderAccount = record.ProviderAccount ?? (object)DBNull.Value
            },
            cancellationToken: ct)).ConfigureAwait(false));
    }

    public async Task<UsageRecord?> GetUsageAsync(string requestId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<UsageRecordEntity>(new CommandDefinition(
            UsageByRequestSql,
            new { RequestId = requestId },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
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
        var items = (await connection.QueryAsync<UsageRecordEntity>(new CommandDefinition(
                UsageSelectSql + whereClause + "order by created_at_utc\nlimit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
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
        return BillingPersistenceMapper.ToDomain(await connection.QueryFirstAsync<ActivityByTokenEntity>(new CommandDefinition(
            """
            with ins as (
                insert into billing.activity_by_token
                    (request_id, api_key_id, api_key_prefix, model_id, router_node_id,
                     input_tokens, output_tokens, cache_tokens, status, created_at_utc, completed_at_utc,
                     provider_name, provider_account)
                values
                    (@RequestId, @ApiKeyId, @ApiKeyPrefix, @ModelId, @RouterNodeId,
                     @InputTokens, @OutputTokens, @CacheTokens, @Status, @CreatedAtUtc, @CompletedAtUtc,
                     @ProviderName, @ProviderAccount)
                on conflict (request_id) do nothing
                returning *
            )
            select request_id as RequestId, api_key_id as ApiKeyId, api_key_prefix as ApiKeyPrefix,
                   model_id as ModelId, router_node_id as RouterNodeId,
                   input_tokens as InputTokens, output_tokens as OutputTokens, cache_tokens as CacheTokens,
                   null::numeric as CostAmount,
                   status as Status, created_at_utc as CreatedAtUtc, completed_at_utc as CompletedAtUtc,
                   provider_name as ProviderName, provider_account as ProviderAccount
            from ins
            union all
            select request_id as RequestId, api_key_id as ApiKeyId, api_key_prefix as ApiKeyPrefix,
                   model_id as ModelId, router_node_id as RouterNodeId,
                   input_tokens as InputTokens, output_tokens as OutputTokens, cache_tokens as CacheTokens,
                   null::numeric as CostAmount,
                   status as Status, created_at_utc as CreatedAtUtc, completed_at_utc as CompletedAtUtc,
                   provider_name as ProviderName, provider_account as ProviderAccount
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
                record.CompletedAtUtc,
                ProviderName = record.ProviderName ?? (object)DBNull.Value,
                ProviderAccount = record.ProviderAccount ?? (object)DBNull.Value
            },
            cancellationToken: ct)).ConfigureAwait(false));
    }

    public async Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        DateTimeOffset? afterCreatedAtUtc = null,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return (Array.Empty<ActivityByTokenRecord>(), false);

        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("ApiKeyIds", apiKeyIds.ToArray());
        parameters.Add("Limit", limit + 1);

        var conditions = new List<string> { "a.api_key_id = any(@ApiKeyIds)" };

        if (fromUtc.HasValue) { conditions.Add("a.created_at_utc >= @FromUtc"); parameters.Add("FromUtc", fromUtc.Value); }
        if (toUtc.HasValue) { conditions.Add("a.created_at_utc <= @ToUtc"); parameters.Add("ToUtc", toUtc.Value); }
        if (!string.IsNullOrEmpty(modelIdFilter)) { conditions.Add("a.model_id = @ModelIdFilter"); parameters.Add("ModelIdFilter", modelIdFilter); }
        if (!string.IsNullOrEmpty(apiKeyIdFilter)) { conditions.Add("a.api_key_id = @ApiKeyIdFilter"); parameters.Add("ApiKeyIdFilter", apiKeyIdFilter); }
        if (afterCreatedAtUtc.HasValue) { conditions.Add("a.created_at_utc < @AfterCreatedAtUtc"); parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value); }

        var items = (await connection.QueryAsync<ActivityByTokenEntity>(new CommandDefinition(
                ActivityByTokenSelectSql +
                "where " + string.Join(" and ", conditions) + "\n" +
                "order by a.created_at_utc desc\nlimit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<ActivitySummary> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return new ActivitySummary(0, 0, 0, 0, MoneyAmount.Rub(0), [], []);

        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("ApiKeyIds", apiKeyIds.ToArray());

        var dateConditions = new List<string> { "a.api_key_id = any(@ApiKeyIds)" };
        if (fromUtc.HasValue) { dateConditions.Add("a.created_at_utc >= @FromUtc"); parameters.Add("FromUtc", fromUtc.Value); }
        if (toUtc.HasValue) { dateConditions.Add("a.created_at_utc <= @ToUtc"); parameters.Add("ToUtc", toUtc.Value); }
        var whereClause = "where " + string.Join(" and ", dateConditions);

        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
            $"""
            select
                count(*) as Requests,
                coalesce(sum(a.input_tokens), 0) as InputTokens,
                coalesce(sum(a.output_tokens), 0) as OutputTokens,
                coalesce(sum(a.cache_tokens), 0) as CacheTokens,
                coalesce(sum(u.cost_amount), 0) as CostRub
            from billing.activity_by_token a
            left join billing.usage_records u on u.request_id = a.request_id
            {whereClause};

            select
                a.model_id as ModelId,
                count(*) as Requests,
                coalesce(sum(a.input_tokens), 0) as InputTokens,
                coalesce(sum(a.output_tokens), 0) as OutputTokens,
                coalesce(sum(a.cache_tokens), 0) as CacheTokens,
                coalesce(sum(u.cost_amount), 0) as CostRub
            from billing.activity_by_token a
            left join billing.usage_records u on u.request_id = a.request_id
            {whereClause}
            group by a.model_id
            order by count(*) desc;

            select
                a.api_key_id as ApiKeyId,
                a.api_key_prefix as ApiKeyPrefix,
                count(*) as Requests,
                coalesce(sum(a.input_tokens), 0) as InputTokens,
                coalesce(sum(a.output_tokens), 0) as OutputTokens,
                coalesce(sum(a.cache_tokens), 0) as CacheTokens,
                coalesce(sum(u.cost_amount), 0) as CostRub
            from billing.activity_by_token a
            left join billing.usage_records u on u.request_id = a.request_id
            {whereClause}
            group by a.api_key_id, a.api_key_prefix
            order by count(*) desc;
            """,
            parameters,
            cancellationToken: ct)).ConfigureAwait(false);

        var totals = await multi.ReadSingleAsync<(long Requests, long InputTokens, long OutputTokens, long CacheTokens, decimal CostRub)>().ConfigureAwait(false);
        var models = (await multi.ReadAsync<ActivityModelSummaryEntity>().ConfigureAwait(false))
            .Select(e => new ActivityModelSummary(
                e.ModelId, e.Requests, e.InputTokens, e.OutputTokens, e.CacheTokens, MoneyAmount.Rub(e.CostRub)))
            .ToList();
        var keys = (await multi.ReadAsync<ActivityKeySummaryEntity>().ConfigureAwait(false))
            .Select(e => new ActivityKeySummary(
                e.ApiKeyId, e.ApiKeyPrefix, e.Requests, e.InputTokens, e.OutputTokens, e.CacheTokens, MoneyAmount.Rub(e.CostRub)))
            .ToList();

        return new ActivitySummary(
            totals.Requests,
            totals.InputTokens,
            totals.OutputTokens,
            totals.CacheTokens,
            MoneyAmount.Rub(totals.CostRub),
            models,
            keys);
    }

    public async Task AddAnomalyAsync(BillingAnomaly anomaly, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(anomaly);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.billing_anomalies
                (id, request_id, type, message, created_at_utc)
            values
                (@Id, @RequestId, @Type, @Message, @CreatedAtUtc);
            """,
            anomaly,
            cancellationToken: ct)).ConfigureAwait(false);
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

        var items = (await connection.QueryAsync<BillingAnomalyEntity>(new CommandDefinition(
                """
                select
                    id as Id,
                    request_id as RequestId,
                    type as Type,
                    message as Message,
                    created_at_utc as CreatedAtUtc
                from billing.billing_anomalies

                """ + afterCondition + "order by created_at_utc\nlimit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<(IReadOnlyCollection<AdminRequestRecord> Items, bool HasMore)> GetAdminUsageRecordsAsync(
        int limit,
        string? afterCursor = null,
        string? modelFilter = null,
        string? providerFilter = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);

        var conditions = new List<string>();
        if (!string.IsNullOrEmpty(afterCursor))
        {
            conditions.Add("a.created_at_utc < @AfterCursor");
            parameters.Add("AfterCursor", DateTimeOffset.Parse(afterCursor, null, System.Globalization.DateTimeStyles.RoundtripKind));
        }
        if (!string.IsNullOrEmpty(modelFilter)) { conditions.Add("a.model_id = @ModelFilter"); parameters.Add("ModelFilter", modelFilter); }
        if (!string.IsNullOrEmpty(providerFilter)) { conditions.Add("a.provider_name = @ProviderFilter"); parameters.Add("ProviderFilter", providerFilter); }
        if (fromUtc.HasValue) { conditions.Add("a.created_at_utc >= @FromUtc"); parameters.Add("FromUtc", fromUtc.Value); }
        if (toUtc.HasValue) { conditions.Add("a.created_at_utc <= @ToUtc"); parameters.Add("ToUtc", toUtc.Value); }

        var whereClause = conditions.Count > 0 ? "where " + string.Join(" and ", conditions) + "\n" : "";

        var items = (await connection.QueryAsync<AdminRequestEntity>(new CommandDefinition(
            $"""
            select
                a.request_id as RequestId,
                coalesce(u.user_id, '') as UserId,
                a.api_key_prefix as ApiKeyPrefix,
                a.model_id as ModelId,
                a.router_node_id as RouterNodeId,
                a.provider_name as ProviderName,
                a.provider_account as ProviderAccount,
                a.input_tokens as InputTokens,
                a.output_tokens as OutputTokens,
                a.cache_tokens as CacheTokens,
                coalesce(u.cost_amount, 0) as CostAmount,
                a.status as Status,
                a.created_at_utc as CreatedAtUtc,
                a.completed_at_utc as CompletedAtUtc
            from billing.activity_by_token a
            left join billing.usage_records u on u.request_id = a.request_id
            {whereClause}order by a.created_at_utc desc
            limit @Limit;
            """,
            parameters,
            cancellationToken: ct)).ConfigureAwait(false))
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
            enabled as Enabled,
            description as Description,
            context_length as ContextLength,
            modalities as Modalities,
            description_source as DescriptionSource
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
            created_at_utc as CreatedAtUtc,
            provider_name as ProviderName,
            provider_account as ProviderAccount
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
            a.completed_at_utc as CompletedAtUtc,
            a.provider_name as ProviderName,
            a.provider_account as ProviderAccount
        from billing.activity_by_token a
        left join billing.usage_records u on u.request_id = a.request_id

        """;
}
