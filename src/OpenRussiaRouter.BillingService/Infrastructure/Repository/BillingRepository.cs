using Dapper;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

public sealed class BillingRepository(IBillingDbConnectionFactory connectionFactory) : IBillingRepository
{
    public async Task<(IReadOnlyCollection<LedgerEntry> Items, bool HasMore)> ListTopUpsAsync(string userId, int limit, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<LedgerEntryEntity>(new CommandDefinition(
                LedgerEntrySelectSql +
                """
                where user_id = @UserId and type in ('VirtualTopUp','ExternalTopUp')
                order by created_at_utc desc, id desc
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
        return MoneyAmount.Usd(amount);
    }

    public async Task<ApiKeySpendLimit?> GetApiKeySpendLimitAsync(string userId, string apiKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ApiKeySpendLimitRow>(new CommandDefinition(
            """
            select user_id as UserId,
                   api_key_id as ApiKeyId,
                   daily_limit_amount as DailyLimitAmount,
                   weekly_limit_amount as WeeklyLimitAmount,
                   created_at_utc as CreatedAtUtc,
                   updated_at_utc as UpdatedAtUtc
            from billing.api_key_spend_limits
            where user_id = @UserId and api_key_id = @ApiKeyId;
            """,
            new { UserId = userId, ApiKeyId = apiKeyId },
            cancellationToken: ct)).ConfigureAwait(false);

        return row is null ? null : ToApiKeySpendLimit(row);
    }

    public async Task UpsertApiKeySpendLimitAsync(ApiKeySpendLimit limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(limit);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.api_key_spend_limits
                (api_key_id, user_id, daily_limit_amount, weekly_limit_amount, currency, created_at_utc, updated_at_utc)
            values
                (@ApiKeyId, @UserId, @DailyLimitAmount, @WeeklyLimitAmount, 'Usd', @CreatedAtUtc, @UpdatedAtUtc)
            on conflict (api_key_id) do update set
                user_id = excluded.user_id,
                daily_limit_amount = excluded.daily_limit_amount,
                weekly_limit_amount = excluded.weekly_limit_amount,
                updated_at_utc = excluded.updated_at_utc;
            """,
            new
            {
                limit.ApiKeyId,
                limit.UserId,
                DailyLimitAmount = limit.DailyLimit?.Amount,
                WeeklyLimitAmount = limit.WeeklyLimit?.Amount,
                limit.CreatedAtUtc,
                limit.UpdatedAtUtc
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task ClearApiKeySpendLimitAsync(string userId, string apiKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from billing.api_key_spend_limits where user_id = @UserId and api_key_id = @ApiKeyId;",
            new { UserId = userId, ApiKeyId = apiKeyId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<ApiKeyLimitSummary>> ListApiKeyLimitSummariesAsync(
        string userId,
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset dayStartUtc,
        DateTimeOffset weekStartUtc,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return Array.Empty<ApiKeyLimitSummary>();

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ApiKeyLimitSummaryRow>(new CommandDefinition(
            """
            with requested as (
                select unnest(cast(@ApiKeyIds as varchar[])) as api_key_id
            ),
            usage_totals as (
                select api_key_id,
                       coalesce(sum(cost_amount) filter (where created_at_utc >= @DayStartUtc), 0) as daily_spent,
                       coalesce(sum(cost_amount), 0) as weekly_spent
                from billing.usage_records
                where user_id = @UserId
                  and api_key_id = any(@ApiKeyIds)
                  and created_at_utc >= @WeekStartUtc
                group by api_key_id
            )
            select r.api_key_id as ApiKeyId,
                   l.daily_limit_amount as DailyLimitAmount,
                   l.weekly_limit_amount as WeeklyLimitAmount,
                   coalesce(u.daily_spent, 0) as DailySpent,
                   coalesce(u.weekly_spent, 0) as WeeklySpent
            from requested r
            left join billing.api_key_spend_limits l
                on l.api_key_id = r.api_key_id and l.user_id = @UserId
            left join usage_totals u
                on u.api_key_id = r.api_key_id;
            """,
            new
            {
                UserId = userId,
                ApiKeyIds = apiKeyIds.Distinct(StringComparer.Ordinal).ToArray(),
                DayStartUtc = dayStartUtc,
                WeekStartUtc = weekStartUtc
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(ToApiKeyLimitSummary).ToArray();
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
                description, context_length, modalities, input_modalities, output_modalities, description_source,
                tag_ids, provider, provider_id,
                supports_reasoning, allowed_reasoning_efforts, default_reasoning_effort, model_type,
                priority
            )
            values (
                @ModelId, @DisplayName, @RouterModelId, @PriceVersion,
                @InputPerMillionAmount, @OutputPerMillionAmount, @CachePerMillionAmount,
                @Currency, @Enabled,
                @Description, @ContextLength, @Modalities, @InputModalities, @OutputModalities, @DescriptionSource,
                @TagIds, @Provider, @ProviderId,
                @SupportsReasoning, @AllowedReasoningEfforts, @DefaultReasoningEffort, @ModelType,
                @Priority
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
                tag_ids = excluded.tag_ids,
                provider = excluded.provider,
                provider_id = excluded.provider_id,
                supports_reasoning = excluded.supports_reasoning,
                allowed_reasoning_efforts = excluded.allowed_reasoning_efforts,
                default_reasoning_effort = excluded.default_reasoning_effort,
                model_type = excluded.model_type,
                priority = excluded.priority,
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
                input_modalities = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.input_modalities
                    ELSE COALESCE(excluded.input_modalities, billing.model_prices.input_modalities)
                END,
                output_modalities = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.output_modalities
                    ELSE COALESCE(excluded.output_modalities, billing.model_prices.output_modalities)
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
                model.InputModalities,
                model.OutputModalities,
                model.DescriptionSource,
                model.Provider,
                model.ProviderId,
                TagIds = model.TagIds?.ToArray() ?? [],
                model.SupportsReasoning,
                AllowedReasoningEfforts = ReasoningEffort.JoinCsv(model.AllowedReasoningEfforts),
                DefaultReasoningEffort = model.DefaultReasoningEffort,
                ModelType = model.ModelType,
                model.Priority
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<ModelPrice?> GetEnabledModelAsync(string modelId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ModelPriceEntity>(new CommandDefinition(
            ModelPriceSelectSql + "where model_id = @ModelId and enabled = true;",
            new { ModelId = modelId },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? BillingPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<ModelPrice?> GetModelAsync(string modelId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ModelPriceEntity>(new CommandDefinition(
            ModelPriceSelectSql + "where model_id = @ModelId or router_model_id = @ModelId limit 1;",
            new { ModelId = modelId },
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

    public async Task<(IReadOnlyCollection<UsageRecord> Items, bool HasMore)> ListUsageAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? afterRequestId = null, string? userId = null, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var conditions = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        if (afterCreatedAtUtc.HasValue && !string.IsNullOrEmpty(afterRequestId))
        {
            conditions.Add("(created_at_utc, request_id) < (@AfterCreatedAtUtc, @AfterRequestId)");
            parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
            parameters.Add("AfterRequestId", afterRequestId);
        }
        else if (afterCreatedAtUtc.HasValue)
        {
            conditions.Add("created_at_utc < @AfterCreatedAtUtc");
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
                UsageSelectSql + whereClause + "order by created_at_utc desc, request_id desc\nlimit @Limit;",
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

    public async Task RecordUsageWithTransactionAsync(ActivityByTokenRecord activity, UsageRecord? usage, LedgerEntry? ledgerEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into billing.activity_by_token
                    (request_id, api_key_id, api_key_prefix, model_id, router_node_id,
                     input_tokens, output_tokens, cache_tokens, status, created_at_utc, completed_at_utc,
                     provider_name, provider_account)
                values
                    (@RequestId, @ApiKeyId, @ApiKeyPrefix, @ModelId, @RouterNodeId,
                     @InputTokens, @OutputTokens, @CacheTokens, @Status, @CreatedAtUtc, @CompletedAtUtc,
                     @ProviderName, @ProviderAccount)
                on conflict (request_id) do nothing;
                """,
                new
                {
                    activity.RequestId,
                    activity.ApiKeyId,
                    activity.ApiKeyPrefix,
                    activity.ModelId,
                    activity.RouterNodeId,
                    activity.Usage.InputTokens,
                    activity.Usage.OutputTokens,
                    activity.Usage.CacheTokens,
                    activity.Status,
                    activity.CreatedAtUtc,
                    activity.CompletedAtUtc,
                    ProviderName = activity.ProviderName ?? (object)DBNull.Value,
                    ProviderAccount = activity.ProviderAccount ?? (object)DBNull.Value
                },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);

            if (usage is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into billing.usage_records
                        (request_id, user_id, api_key_id, model_id, price_version, router_node_id,
                         input_tokens, output_tokens, cache_tokens, cost_amount, cost_currency, status, created_at_utc,
                         provider_name, provider_account)
                    values
                        (@RequestId, @UserId, @ApiKeyId, @ModelId, @PriceVersion, @RouterNodeId,
                         @InputTokens, @OutputTokens, @CacheTokens, @CostAmount, @CostCurrency, @Status, @CreatedAtUtc,
                         @ProviderName, @ProviderAccount)
                    on conflict (request_id) do nothing;
                    """,
                    new
                    {
                        usage.RequestId,
                        usage.UserId,
                        usage.ApiKeyId,
                        usage.ModelId,
                        usage.PriceVersion,
                        usage.RouterNodeId,
                        usage.Usage.InputTokens,
                        usage.Usage.OutputTokens,
                        usage.Usage.CacheTokens,
                        CostAmount = usage.Cost.Amount,
                        CostCurrency = usage.Cost.Currency.ToString(),
                        usage.Status,
                        usage.CreatedAtUtc,
                        ProviderName = usage.ProviderName ?? (object)DBNull.Value,
                        ProviderAccount = usage.ProviderAccount ?? (object)DBNull.Value
                    },
                    transaction,
                    cancellationToken: ct)).ConfigureAwait(false);
            }

            if (ledgerEntry is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into billing.ledger_entries
                        (id, user_id, type, amount, currency, idempotency_key, reason, created_at_utc)
                    values
                        (@Id, @UserId, @Type, @Amount, @Currency, @IdempotencyKey, @Reason, @CreatedAtUtc)
                    on conflict (idempotency_key) do nothing;
                    """,
                    new
                    {
                        ledgerEntry.Id,
                        ledgerEntry.UserId,
                        Type = ledgerEntry.Type.ToString(),
                        ledgerEntry.Amount.Amount,
                        Currency = ledgerEntry.Amount.Currency.ToString(),
                        ledgerEntry.IdempotencyKey,
                        ledgerEntry.Reason,
                        ledgerEntry.CreatedAtUtc
                    },
                    transaction,
                    cancellationToken: ct)).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore, int TotalCount)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        int limit,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        DateTimeOffset? afterCreatedAtUtc = null,
        string? afterRequestId = null,
        int offset = 0,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return (Array.Empty<ActivityByTokenRecord>(), false, 0);

        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("ApiKeyIds", apiKeyIds.ToArray());
        parameters.Add("Limit", limit + 1);
        parameters.Add("Offset", offset);

        var conditions = new List<string> { "a.api_key_id = any(@ApiKeyIds)" };

        if (fromUtc.HasValue) { conditions.Add("a.created_at_utc >= @FromUtc"); parameters.Add("FromUtc", fromUtc.Value); }
        if (toUtc.HasValue) { conditions.Add("a.created_at_utc <= @ToUtc"); parameters.Add("ToUtc", toUtc.Value); }
        if (!string.IsNullOrEmpty(modelIdFilter)) { conditions.Add("a.model_id = @ModelIdFilter"); parameters.Add("ModelIdFilter", modelIdFilter); }
        if (!string.IsNullOrEmpty(apiKeyIdFilter)) { conditions.Add("a.api_key_id = @ApiKeyIdFilter"); parameters.Add("ApiKeyIdFilter", apiKeyIdFilter); }
        if (afterCreatedAtUtc.HasValue && !string.IsNullOrEmpty(afterRequestId))
        {
            conditions.Add("(a.created_at_utc, a.request_id) < (@AfterCreatedAtUtc, @AfterRequestId)");
            parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
            parameters.Add("AfterRequestId", afterRequestId);
        }
        else if (afterCreatedAtUtc.HasValue)
        {
            conditions.Add("a.created_at_utc < @AfterCreatedAtUtc");
            parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
        }

        var whereClause = "where " + string.Join(" and ", conditions);

        var countSql = $"select count(*) from billing.activity_by_token a {whereClause}";
        var totalCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: ct));

        var items = (await connection.QueryAsync<ActivityByTokenEntity>(new CommandDefinition(
                ActivityByTokenSelectSql +
                whereClause + "\n" +
                "order by a.created_at_utc desc, a.request_id desc\n" +
                "offset @Offset limit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore, totalCount);
    }

    public async Task<ActivitySummary> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return new ActivitySummary(0, 0, 0, 0, MoneyAmount.Usd(0), [], []);

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
                coalesce(sum(u.cost_amount), 0) as CostUsd
            from billing.activity_by_token a
            left join billing.usage_records u on u.request_id = a.request_id
            {whereClause};

            select
                a.model_id as ModelId,
                count(*) as Requests,
                coalesce(sum(a.input_tokens), 0) as InputTokens,
                coalesce(sum(a.output_tokens), 0) as OutputTokens,
                coalesce(sum(a.cache_tokens), 0) as CacheTokens,
                coalesce(sum(u.cost_amount), 0) as CostUsd
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
                coalesce(sum(u.cost_amount), 0) as CostUsd
            from billing.activity_by_token a
            left join billing.usage_records u on u.request_id = a.request_id
            {whereClause}
            group by a.api_key_id, a.api_key_prefix
            order by count(*) desc;
            """,
            parameters,
            cancellationToken: ct)).ConfigureAwait(false);

        var totals = await multi.ReadSingleAsync<(long Requests, long InputTokens, long OutputTokens, long CacheTokens, decimal CostUsd)>().ConfigureAwait(false);
        var models = (await multi.ReadAsync<ActivityModelSummaryEntity>().ConfigureAwait(false))
            .Select(e => new ActivityModelSummary(
                e.ModelId, e.Requests, e.InputTokens, e.OutputTokens, e.CacheTokens, MoneyAmount.Usd(e.CostUsd)))
            .ToList();
        var keys = (await multi.ReadAsync<ActivityKeySummaryEntity>().ConfigureAwait(false))
            .Select(e => new ActivityKeySummary(
                e.ApiKeyId, e.ApiKeyPrefix, e.Requests, e.InputTokens, e.OutputTokens, e.CacheTokens, MoneyAmount.Usd(e.CostUsd)))
            .ToList();

        return new ActivitySummary(
            totals.Requests,
            totals.InputTokens,
            totals.OutputTokens,
            totals.CacheTokens,
            MoneyAmount.Usd(totals.CostUsd),
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

    public async Task<(IReadOnlyCollection<BillingAnomaly> Items, bool HasMore)> ListAnomaliesAsync(int limit, DateTimeOffset? afterCreatedAtUtc = null, string? afterId = null, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterCreatedAtUtc.HasValue && !string.IsNullOrEmpty(afterId))
        {
            afterCondition = "where (created_at_utc, id) < (@AfterCreatedAtUtc, @AfterId)\n";
            parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
            parameters.Add("AfterId", afterId);
        }
        else if (afterCreatedAtUtc.HasValue)
        {
            afterCondition = "where created_at_utc < @AfterCreatedAtUtc\n";
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

                """ + afterCondition + "order by created_at_utc desc, id desc\nlimit @Limit;",
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
            var parts = afterCursor.Split('|', 2);
            if (parts.Length == 2)
            {
                conditions.Add("(a.created_at_utc, a.request_id) < (@AfterCursorDate, @AfterCursorId)");
                parameters.Add("AfterCursorDate", DateTimeOffset.Parse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind));
                parameters.Add("AfterCursorId", parts[1]);
            }
            else
            {
                conditions.Add("a.created_at_utc < @AfterCursorDate");
                parameters.Add("AfterCursorDate", DateTimeOffset.Parse(afterCursor, null, System.Globalization.DateTimeStyles.RoundtripKind));
            }
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
            {whereClause}order by a.created_at_utc desc, a.request_id desc
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

    public async Task<IReadOnlyCollection<Tag>> ListTagsAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<TagEntity>(new CommandDefinition(
            """
            select
                id as Id,
                display_name as DisplayName,
                color as Color
            from billing.tags
            order by display_name;
            """,
            cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();
    }

    public async Task UpsertTagAsync(Tag tag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.tags (id, display_name, color)
            values (@Id, @DisplayName, @Color)
            on conflict (id) do update set
                display_name = excluded.display_name,
                color = excluded.color;
            """,
            new { tag.Id, tag.DisplayName, tag.Color },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteTagAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update billing.model_prices
            set tag_ids = array_remove(tag_ids, @Id)
            where tag_ids @> array[@Id]::text[];

            delete from billing.tags where id = @Id;
            """,
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<string?> GetPlatformSettingAsync(string key, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select value from billing.platform_settings where key = @Key;",
            new { Key = key },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task SetPlatformSettingAsync(string key, string value, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.platform_settings (key, value, updated_at_utc)
            values (@Key, @Value, now())
            on conflict (key) do update set
                value = excluded.value,
                updated_at_utc = now();
            """,
            new { Key = key, Value = value },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<Payment> CreatePaymentAsync(Payment payment, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.payments (
                id, user_id, client_token, provider, provider_payment_id, order_id,
                display_amount, display_currency, provider_amount, provider_currency,
                credited_usd_amount, fx_rate, fx_source, fx_at_utc, status, cancel_reason,
                lease_owner, lease_until_utc, created_at_utc, updated_at_utc, expires_at_utc,
                confirmed_at_utc, canceled_at_utc)
            values (
                @Id, @UserId, @ClientToken, @Provider, @ProviderPaymentId, @OrderId,
                @DisplayAmount, @DisplayCurrency, @ProviderAmount, @ProviderCurrency,
                @CreditedUsdAmount, @FxRate, @FxSource, @FxAtUtc, @Status, @CancelReason,
                @LeaseOwner, @LeaseUntilUtc, @CreatedAtUtc, @UpdatedAtUtc, @ExpiresAtUtc,
                @ConfirmedAtUtc, @CanceledAtUtc)
            on conflict (user_id, client_token) do nothing;
            """,
            PaymentParameters(payment), cancellationToken: ct)).ConfigureAwait(false);
        var entity = await connection.QuerySingleAsync<PaymentEntity>(new CommandDefinition(
            PaymentSelectSql + "where user_id=@UserId and client_token=@ClientToken;",
            new { payment.UserId, payment.ClientToken }, cancellationToken: ct)).ConfigureAwait(false);
        return BillingPersistenceMapper.ToDomain(entity);
    }

    public async Task<bool> AttachProviderRefAsync(string paymentId, string providerPaymentId, string? payUrl, DateTimeOffset? expiresAt, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            update billing.payments set provider_payment_id=@ProviderPaymentId, pay_url=@PayUrl,
                expires_at_utc=@ExpiresAt, updated_at_utc=@Now
            where id=@PaymentId and status='Created';
            """,
            new { PaymentId = paymentId, ProviderPaymentId = providerPaymentId, PayUrl = payUrl, ExpiresAt = expiresAt, Now = DateTimeOffset.UtcNow },
            cancellationToken: ct)).ConfigureAwait(false) == 1;
    }

    public async Task<Payment?> GetPaymentAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var entity = await connection.QuerySingleOrDefaultAsync<PaymentEntity>(new CommandDefinition(
            PaymentSelectSql + "where id=@Id;", new { Id = id }, cancellationToken: ct)).ConfigureAwait(false);
        return entity is null ? null : BillingPersistenceMapper.ToDomain(entity);
    }

    public async Task<(IReadOnlyCollection<Payment> Items, bool HasMore)> ListPaymentsAsync(string userId, int limit, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<PaymentEntity>(new CommandDefinition(
            PaymentSelectSql + "where user_id=@UserId order by created_at_utc desc limit @Limit;",
            new { UserId = userId, Limit = limit + 1 }, cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain).ToArray();
        var hasMore = items.Length > limit;
        return (hasMore ? items.Take(limit).ToArray() : items, hasMore);
    }

    public async Task<ConfirmOutcome> ConfirmPaymentAtomicAsync(PaymentEventInput evt, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var eventRow = await InsertPaymentEventAsync(evt, nowUtc, ct).ConfigureAwait(false);
        if (eventRow is null)
            return new ConfirmOutcome(false, null, null, 0, ErrorCodes.PaymentNotFound);

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var outcome = await ConfirmPaymentAtomicAsync(connection, transaction, eventRow, evt, nowUtc, ct).ConfigureAwait(false);
            transaction.Commit();
            return outcome;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<ApplyOutcome> ApplyPaymentEventAtomicAsync(PaymentEventInput evt, DateTimeOffset nowUtc, bool autoReverseChargeback = false, CancellationToken ct = default)
    {
        var eventRow = await InsertPaymentEventAsync(evt, nowUtc, ct).ConfigureAwait(false);
        if (eventRow is null)
            return new ApplyOutcome(false, null, null, ErrorCodes.PaymentNotFound);
        var target = evt.EventType.ToLowerInvariant() switch
        {
            "canceled" => PaymentStatus.Canceled,
            "expired" => PaymentStatus.Expired,
            "refunded" => PaymentStatus.Refunded,
            "chargeback" => PaymentStatus.ChargedBack,
            _ => (PaymentStatus?)null
        };
        if (target is null)
            return new ApplyOutcome(false, null, null, ErrorCodes.PaymentInvalidTransition);

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var outcome = await ApplyPaymentEventAtomicAsync(
                connection, transaction, eventRow, evt, target.Value, nowUtc, autoReverseChargeback, ct).ConfigureAwait(false);
            transaction.Commit();
            return outcome;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyCollection<PaymentEvent>> ListUnprocessedEventsAsync(int limit, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<PaymentEventEntity>(new CommandDefinition(
            PaymentEventSelectSql + "where processed=false order by received_at_utc limit @Limit;",
            new { Limit = limit }, cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain).ToArray();
    }

    public async Task<IReadOnlyCollection<Payment>> AcquireStalePaymentsAsync(string workerId, TimeSpan lease, DateTimeOffset olderThanUtc, int limit, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var payments = (await connection.QueryAsync<PaymentEntity>(new CommandDefinition(
            """
            with updated as (
                update billing.payments
                set lease_owner = @WorkerId, lease_until_utc = @LeaseUntil, updated_at_utc = @Now
                where id in (
                    select id from billing.payments
                    where status = 'Created'
                      and created_at_utc < @OlderThanUtc
                      and (lease_owner is null or lease_until_utc < @Now)
                    order by created_at_utc
                    limit @Limit
                    for update skip locked
                )
                returning *
            )
            select id as Id, user_id as UserId, provider as Provider, provider_payment_id as ProviderPaymentId,
                order_id as OrderId, client_token as ClientToken, display_amount as DisplayAmount,
                display_currency as DisplayCurrency, provider_amount as ProviderAmount, provider_currency as ProviderCurrency,
                credited_usd_amount as CreditedUsdAmount, fx_rate as FxRate, fx_source as FxSource, fx_at_utc as FxAtUtc,
                status as Status, cancel_reason as CancelReason, pay_url as PayUrl, lease_owner as LeaseOwner,
                lease_until_utc as LeaseUntilUtc, created_at_utc as CreatedAtUtc, updated_at_utc as UpdatedAtUtc,
                expires_at_utc as ExpiresAtUtc, confirmed_at_utc as ConfirmedAtUtc, canceled_at_utc as CanceledAtUtc
            from updated;
            """,
            new { WorkerId = workerId, LeaseUntil = now.Add(lease), Now = now, OlderThanUtc = olderThanUtc, Limit = limit },
            transaction, cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain).ToArray();
        transaction.Commit();
        return payments;
    }

    public async Task<int> AcquireUnprocessedEventsAsync(int limit, DateTimeOffset nowUtc, bool autoReverseChargeback = false, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var events = (await connection.QueryAsync<PaymentEventEntity>(new CommandDefinition(
                PaymentEventSelectSql + "where processed=false order by received_at_utc for update skip locked limit @Limit;",
                new { Limit = limit }, transaction, cancellationToken: ct)).ConfigureAwait(false)).ToArray();

            foreach (var eventRow in events)
            {
                var evt = ToRecoveryInput(eventRow);
                if (string.Equals(eventRow.EventType, "paid", StringComparison.OrdinalIgnoreCase))
                {
                    await ConfirmPaymentAtomicAsync(connection, transaction, eventRow, evt, nowUtc, ct).ConfigureAwait(false);
                    continue;
                }

                var target = ParseTargetStatus(eventRow.EventType);
                if (target is null)
                {
                    await MarkEventProcessedAsync(connection, transaction, eventRow.Id, nowUtc, ct).ConfigureAwait(false);
                    continue;
                }

                await ApplyPaymentEventAtomicAsync(
                    connection, transaction, eventRow, evt, target.Value, nowUtc, autoReverseChargeback, ct).ConfigureAwait(false);
            }

            transaction.Commit();
            return events.Length;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task<ConfirmOutcome> ConfirmPaymentAtomicAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        PaymentEventEntity eventRow,
        PaymentEventInput evt,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var updatedId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            update billing.payments set status='Paid', confirmed_at_utc=@Now, updated_at_utc=@Now,
                lease_owner=null, lease_until_utc=null
            where id=@PaymentId and status='Created'
                and provider_amount=@ConfirmedAmount and upper(provider_currency)=upper(@ConfirmedCurrency)
            returning id;
            """,
            new { PaymentId = eventRow.PaymentId, evt.ConfirmedAmount, evt.ConfirmedCurrency, Now = nowUtc },
            transaction, cancellationToken: ct)).ConfigureAwait(false);
        var payment = await connection.QuerySingleOrDefaultAsync<PaymentEntity>(new CommandDefinition(
            PaymentSelectSql + "where id=@Id;", new { Id = eventRow.PaymentId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        if (payment is null)
            return new ConfirmOutcome(false, null, null, 0, ErrorCodes.PaymentNotFound);

        string? error = null;
        string? ledgerId = null;
        var confirmed = updatedId is not null;
        if (updatedId is not null)
        {
            ledgerId = await InsertLedgerAsync(connection, transaction, payment, "payment", LedgerEntryType.ExternalTopUp,
                payment.CreditedUsdAmount, nowUtc, ct).ConfigureAwait(false);
        }
        else if (payment.Status == PaymentStatus.Paid)
        {
            ledgerId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "select id from billing.ledger_entries where idempotency_key=@Key;",
                new { Key = $"payment:{payment.Id}" }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        }
        else if (payment.Status != PaymentStatus.Created)
            error = ErrorCodes.PaymentInvalidTransition;
        else
        {
            error = ErrorCodes.PaymentAmountMismatch;
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into billing.billing_anomalies (id, request_id, type, message, created_at_utc)
                values (@Id, @PaymentId, 'payment_amount_mismatch', 'Confirmed amount or currency did not match payment.', @Now);
                """,
                new { Id = Guid.NewGuid().ToString("N"), PaymentId = payment.Id, Now = nowUtc },
                transaction, cancellationToken: ct)).ConfigureAwait(false);
        }

        await MarkEventProcessedAsync(connection, transaction, eventRow.Id, nowUtc, ct).ConfigureAwait(false);
        var balance = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "select coalesce(sum(amount),0) from billing.ledger_entries where user_id=@UserId;",
            new { payment.UserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        return new ConfirmOutcome(confirmed, BillingPersistenceMapper.ToDomain(payment), ledgerId, balance, error);
    }

    private static async Task<ApplyOutcome> ApplyPaymentEventAtomicAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        PaymentEventEntity eventRow,
        PaymentEventInput evt,
        PaymentStatus target,
        DateTimeOffset nowUtc,
        bool autoReverseChargeback,
        CancellationToken ct)
    {
        var fromStatus = target is PaymentStatus.Canceled or PaymentStatus.Expired ? "Created" : "Paid";
        var updatedId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            update billing.payments set status=@Target, cancel_reason=@Reason, updated_at_utc=@Now,
                canceled_at_utc=case when @Target in ('Canceled','Expired') then @Now else canceled_at_utc end,
                lease_owner=null, lease_until_utc=null
            where id=@PaymentId and status=@FromStatus
            returning id;
            """,
            new { PaymentId = eventRow.PaymentId, Target = target.ToString(), FromStatus = fromStatus, evt.Reason, Now = nowUtc },
            transaction, cancellationToken: ct)).ConfigureAwait(false);
        if (updatedId is null)
        {
            var current = await connection.QuerySingleOrDefaultAsync<PaymentEntity>(new CommandDefinition(
                PaymentSelectSql + "where id=@Id;", new { Id = eventRow.PaymentId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
            await MarkEventProcessedAsync(connection, transaction, eventRow.Id, nowUtc, ct).ConfigureAwait(false);
            return new ApplyOutcome(false, current is null ? null : BillingPersistenceMapper.ToDomain(current), null, ErrorCodes.PaymentInvalidTransition);
        }

        var updated = await connection.QuerySingleAsync<PaymentEntity>(new CommandDefinition(
            PaymentSelectSql + "where id=@Id;", new { Id = eventRow.PaymentId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        string? ledgerId = null;
        if (target == PaymentStatus.Refunded || (target == PaymentStatus.ChargedBack && autoReverseChargeback))
        {
            var prefix = target == PaymentStatus.Refunded ? "refund" : "chargeback";
            ledgerId = await InsertLedgerAsync(connection, transaction, updated, prefix, LedgerEntryType.Correction,
                -updated.CreditedUsdAmount, nowUtc, ct).ConfigureAwait(false);
        }

        await MarkEventProcessedAsync(connection, transaction, eventRow.Id, nowUtc, ct).ConfigureAwait(false);
        return new ApplyOutcome(true, BillingPersistenceMapper.ToDomain(updated), ledgerId, null);
    }

    private static PaymentEventInput ToRecoveryInput(PaymentEventEntity evt) =>
        new(evt.Provider, evt.ProviderPaymentId, evt.OrderId, evt.EventType, evt.ConfirmedAmount,
            evt.ConfirmedCurrency, evt.Reason, evt.ProviderEventId, null, evt.SignatureOk);

    private static PaymentStatus? ParseTargetStatus(string eventType) => eventType.ToLowerInvariant() switch
    {
        "canceled" => PaymentStatus.Canceled,
        "expired" => PaymentStatus.Expired,
        "refunded" => PaymentStatus.Refunded,
        "chargeback" => PaymentStatus.ChargedBack,
        _ => null
    };

    private async Task<PaymentEventEntity?> InsertPaymentEventAsync(PaymentEventInput evt, DateTimeOffset nowUtc, CancellationToken ct)
    {
        using var connection = connectionFactory.CreateConnection();
        var paymentId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            select id from billing.payments where provider=@Provider and
                ((@ProviderPaymentId is not null and provider_payment_id=@ProviderPaymentId)
                 or (@OrderId is not null and order_id=@OrderId))
            limit 1;
            """,
            new { evt.Provider, evt.ProviderPaymentId, evt.OrderId }, cancellationToken: ct)).ConfigureAwait(false);
        if (paymentId is null)
            return null;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.payment_events (
                id, payment_id, provider, provider_event_id, provider_payment_id, event_type,
                order_id, confirmed_amount, confirmed_currency, reason, raw_payload, signature_ok,
                processed, attempts, received_at_utc)
            values (@Id, @PaymentId, @Provider, @ProviderEventId, @ProviderPaymentId, @EventType,
                @OrderId, @ConfirmedAmount, @ConfirmedCurrency, @Reason, @RawPayload, @SignatureOk,
                false, 0, @Now)
            on conflict do nothing;
            """,
            new
            {
                Id = Guid.NewGuid().ToString("N"), PaymentId = paymentId, evt.Provider, evt.ProviderPaymentId, evt.OrderId,
                evt.ProviderEventId, evt.EventType, evt.ConfirmedAmount, evt.ConfirmedCurrency,
                evt.Reason, evt.RawPayload, evt.SignatureOk, Now = nowUtc
            }, cancellationToken: ct)).ConfigureAwait(false);
        return await connection.QuerySingleAsync<PaymentEventEntity>(new CommandDefinition(
            PaymentEventSelectSql + """
            where ((@ProviderEventId is not null and provider=@Provider and provider_event_id=@ProviderEventId)
                or (@ProviderEventId is null and payment_id=@PaymentId and event_type=@EventType))
            limit 1;
            """,
            new { PaymentId = paymentId, evt.Provider, evt.ProviderEventId, evt.EventType },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task<string> InsertLedgerAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction,
        PaymentEntity payment, string prefix, LedgerEntryType type, decimal amount, DateTimeOffset nowUtc, CancellationToken ct)
    {
        return await connection.QuerySingleAsync<string>(new CommandDefinition(
            """
            with ins as (
                insert into billing.ledger_entries (id,user_id,type,amount,currency,idempotency_key,reason,created_at_utc)
                values (@Id,@UserId,@Type,@Amount,'Usd',@Key,@Reason,@Now)
                on conflict (idempotency_key) do nothing returning id
            )
            select id from ins union all select id from billing.ledger_entries where idempotency_key=@Key limit 1;
            """,
            new
            {
                Id = Guid.NewGuid().ToString("N"), payment.UserId, Type = type.ToString(), Amount = amount,
                Key = $"{prefix}:{payment.Id}", Reason = $"{type} payment {payment.Id}", Now = nowUtc
            }, transaction, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static ApiKeySpendLimit ToApiKeySpendLimit(ApiKeySpendLimitRow row) => new(
        row.UserId,
        row.ApiKeyId,
        row.DailyLimitAmount.HasValue ? MoneyAmount.Usd(row.DailyLimitAmount.Value) : null,
        row.WeeklyLimitAmount.HasValue ? MoneyAmount.Usd(row.WeeklyLimitAmount.Value) : null,
        row.CreatedAtUtc,
        row.UpdatedAtUtc);

    private static ApiKeyLimitSummary ToApiKeyLimitSummary(ApiKeyLimitSummaryRow row)
    {
        var dailyLimit = row.DailyLimitAmount.HasValue ? MoneyAmount.Usd(row.DailyLimitAmount.Value) : (MoneyAmount?)null;
        var weeklyLimit = row.WeeklyLimitAmount.HasValue ? MoneyAmount.Usd(row.WeeklyLimitAmount.Value) : (MoneyAmount?)null;
        var dailyRemaining = MoneyAmount.Usd(dailyLimit.HasValue ? Math.Max(dailyLimit.Value.Amount - row.DailySpent, 0m) : 0m);
        var weeklyRemaining = MoneyAmount.Usd(weeklyLimit.HasValue ? Math.Max(weeklyLimit.Value.Amount - row.WeeklySpent, 0m) : 0m);

        return new ApiKeyLimitSummary(
            row.ApiKeyId,
            dailyLimit,
            weeklyLimit,
            MoneyAmount.Usd(row.DailySpent),
            MoneyAmount.Usd(row.WeeklySpent),
            dailyRemaining,
            weeklyRemaining,
            dailyLimit.HasValue && row.DailySpent >= dailyLimit.Value.Amount,
            weeklyLimit.HasValue && row.WeeklySpent >= weeklyLimit.Value.Amount);
    }

    private static Task<int> MarkEventProcessedAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction,
        string eventId, DateTimeOffset nowUtc, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            "update billing.payment_events set processed=true, attempts=attempts+1, processed_at_utc=@Now where id=@Id;",
            new { Id = eventId, Now = nowUtc }, transaction, cancellationToken: ct));

    private static object PaymentParameters(Payment payment) => new
    {
        payment.Id, payment.UserId, payment.ClientToken, payment.Provider, payment.ProviderPaymentId, payment.OrderId,
        payment.DisplayAmount, payment.DisplayCurrency, payment.ProviderAmount, payment.ProviderCurrency,
        CreditedUsdAmount = payment.CreditedUsd.Amount, payment.FxRate, payment.FxSource, payment.FxAtUtc,
        Status = payment.Status.ToString(), payment.CancelReason, payment.LeaseOwner, payment.LeaseUntilUtc,
        payment.CreatedAtUtc, payment.UpdatedAtUtc, payment.ExpiresAtUtc, payment.ConfirmedAtUtc, payment.CanceledAtUtc,
        payment.PayUrl
    };

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
            billing.model_prices.display_name as DisplayName,
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
            input_modalities as InputModalities,
            output_modalities as OutputModalities,
            description_source as DescriptionSource,
            tag_ids as TagIds,
            coalesce(model_provider.display_name, billing.model_prices.provider) as Provider,
            provider_id as ProviderId,
            supports_reasoning as SupportsReasoning,
            allowed_reasoning_efforts as AllowedReasoningEfforts,
            default_reasoning_effort as DefaultReasoningEffort,
            model_type as ModelType,
            priority as Priority
        from billing.model_prices
        left join billing.model_providers model_provider
            on model_provider.id = billing.model_prices.provider_id

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

    private const string PaymentSelectSql = """
        select id as Id, user_id as UserId, provider as Provider, provider_payment_id as ProviderPaymentId,
            order_id as OrderId, client_token as ClientToken, display_amount as DisplayAmount,
            display_currency as DisplayCurrency, provider_amount as ProviderAmount, provider_currency as ProviderCurrency,
            credited_usd_amount as CreditedUsdAmount, fx_rate as FxRate, fx_source as FxSource, fx_at_utc as FxAtUtc,
            status as Status, cancel_reason as CancelReason, pay_url as PayUrl, lease_owner as LeaseOwner, lease_until_utc as LeaseUntilUtc,
            created_at_utc as CreatedAtUtc, updated_at_utc as UpdatedAtUtc, expires_at_utc as ExpiresAtUtc,
            confirmed_at_utc as ConfirmedAtUtc, canceled_at_utc as CanceledAtUtc
        from billing.payments

        """;

    private const string PaymentEventSelectSql = """
        select id as Id, payment_id as PaymentId, provider as Provider, provider_event_id as ProviderEventId,
            provider_payment_id as ProviderPaymentId, event_type as EventType, order_id as OrderId,
            confirmed_amount as ConfirmedAmount, confirmed_currency as ConfirmedCurrency, reason as Reason,
            raw_payload as RawPayload, signature_ok as SignatureOk, processed as Processed, attempts as Attempts,
            received_at_utc as ReceivedAtUtc, processed_at_utc as ProcessedAtUtc
        from billing.payment_events

        """;

    private sealed class ApiKeySpendLimitRow
    {
        public string UserId { get; set; } = "";
        public string ApiKeyId { get; set; } = "";
        public decimal? DailyLimitAmount { get; set; }
        public decimal? WeeklyLimitAmount { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    private sealed class ApiKeyLimitSummaryRow
    {
        public string ApiKeyId { get; set; } = "";
        public decimal? DailyLimitAmount { get; set; }
        public decimal? WeeklyLimitAmount { get; set; }
        public decimal DailySpent { get; set; }
        public decimal WeeklySpent { get; set; }
    }

    private const string ModelProviderSelectSql =
        """
        select id as Id, display_name as DisplayName, description_ru as DescriptionRu,
               description_en as DescriptionEn, logo_bytes as LogoBytes,
               logo_content_type as LogoContentType,
               created_at_utc as CreatedAtUtc, updated_at_utc as UpdatedAtUtc
        from billing.model_providers
        """;

    public async Task<IReadOnlyCollection<ModelProvider>> ListModelProvidersAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ModelProviderEntity>(new CommandDefinition(
            ModelProviderSelectSql + " order by display_name;",
            cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();
        return items;
    }

    public async Task<ModelProvider?> GetModelProviderAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var entity = await connection.QuerySingleOrDefaultAsync<ModelProviderEntity>(new CommandDefinition(
            ModelProviderSelectSql + " where id = @Id;",
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
        return entity is null ? null : BillingPersistenceMapper.ToDomain(entity);
    }

    public async Task UpsertModelProviderAsync(ModelProvider provider, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.model_providers (id, display_name, description_ru, description_en, logo_bytes, logo_content_type, created_at_utc, updated_at_utc)
            values (@Id, @DisplayName, @DescriptionRu, @DescriptionEn, @LogoBytes, @LogoContentType, @CreatedAtUtc, @UpdatedAtUtc)
            on conflict (id) do update set
                display_name = excluded.display_name,
                description_ru = excluded.description_ru,
                description_en = excluded.description_en,
                logo_bytes = excluded.logo_bytes,
                logo_content_type = excluded.logo_content_type,
                updated_at_utc = excluded.updated_at_utc;
            """,
            BillingPersistenceMapper.ToEntity(provider),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<ModelProvider?> GetModelProviderLogoAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var entity = await connection.QuerySingleOrDefaultAsync<ModelProviderEntity>(new CommandDefinition(
            ModelProviderSelectSql + " where id = @Id;",
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
        return entity is null ? null : BillingPersistenceMapper.ToDomain(entity);
    }

    public async Task DeleteModelProviderAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update billing.model_prices set provider_id = null where provider_id = @Id;
            delete from billing.model_providers where id = @Id;
            """,
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
    }
}
