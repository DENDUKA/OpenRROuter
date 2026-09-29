using Dapper;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BillingService.Domain;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

public sealed partial class BillingRepository
{
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

    public async Task<bool> RecordUsageWithTransactionAsync(ActivityByTokenRecord activity, UsageRecord? usage, LedgerEntry? ledgerEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var firstUsageDetected = false;
        using var connection = connectionFactory.CreateConnection();
        var npgsqlConn = (Npgsql.NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);

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
            var insertedUsageId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                """
                insert into billing.usage_records
                    (request_id, user_id, api_key_id, model_id, price_version, router_node_id,
                     input_tokens, output_tokens, cache_tokens, cost_amount, cost_currency, status, created_at_utc,
                     provider_name, provider_account)
                values
                    (@RequestId, @UserId, @ApiKeyId, @ModelId, @PriceVersion, @RouterNodeId,
                     @InputTokens, @OutputTokens, @CacheTokens, @CostAmount, @CostCurrency, @Status, @CreatedAtUtc,
                     @ProviderName, @ProviderAccount)
                on conflict (request_id) do nothing
                returning request_id;
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

            if (insertedUsageId is not null)
            {
                var isFirstUsage = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    """
                    select not exists (
                        select 1
                        from billing.usage_records
                        where user_id = @UserId and request_id <> @RequestId
                    );
                    """,
                    new { usage.UserId, usage.RequestId },
                    transaction,
                    cancellationToken: ct)).ConfigureAwait(false);

                if (isFirstUsage)
                {
                    firstUsageDetected = true;
                    await EnqueueYandexMetrikaOfflineConversionAsync(
                        connection,
                        transaction,
                        $"first_api_request:{usage.UserId}",
                        usage.UserId,
                        YandexMetrikaOfflineConversionTargets.FirstApiRequest,
                        usage.CreatedAtUtc,
                        price: null,
                        currency: null,
                        ct).ConfigureAwait(false);
                }
            }
        }

        if (ledgerEntry is not null)
        {
            // T-069: Four-step CTE ensures atomicity, idempotency and a balance that never
            // gets stuck positive-but-unspendable:
            // 1) target: read the current balance FOR UPDATE — concurrent charges of the same user
            //    serialize here and each one sees the balance left by the previous commit;
            //    without the lock two parallel requests both collect the same residual and the
            //    balance goes negative
            // 2) charge: cap the debit at the remaining balance (greatest(@Amount, -balance)) so
            //    the balance can never go negative and the residual is collected in full. The old
            //    "all-or-nothing" gate let a balance land on a tiny positive remainder that no
            //    charge could ever cover, so the pre-check (balance > 0) kept authorizing requests
            //    forever while no ledger entry was written (fail-open free usage).
            // 3) ledger: insert the (capped) entry; ON CONFLICT DO NOTHING makes it a no-op on
            //    an idempotent repeat
            // 4) balance: debit ONLY if ledger actually inserted a row (EXISTS check)
            //
            // This guarantees: (a) double-calling with the same requestId debits only once,
            // (b) the balance ends at exactly 0 when the charge exceeded the remainder, so the
            //     next ApiGateway pre-check returns InsufficientBalance instead of looping.
            var newBalance = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                """
                with target as (
                    select balance_amount from billing.user_balances where user_id = @UserId
                    for update
                ),
                charge as (
                    select greatest(@Amount, -balance_amount) as amount from target
                ),
                ledger as (
                    insert into billing.ledger_entries
                        (id, user_id, type, amount, currency, idempotency_key, reason, created_at_utc, is_user_payment)
                    select @Id, @UserId, @Type, c.amount, @Currency, @IdempotencyKey, @Reason, @CreatedAtUtc, @IsUserPayment
                    from charge c
                    on conflict (idempotency_key) do nothing
                    returning id
                ),
                balance as (
                    update billing.user_balances
                    set balance_amount = balance_amount + (select amount from charge), updated_at_utc = now()
                    where user_id = @UserId and exists (select 1 from ledger)
                    returning balance_amount
                )
                select balance_amount from balance;
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
                    ledgerEntry.CreatedAtUtc,
                    ledgerEntry.IsUserPayment
                },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);

            // T-069: newBalance is null when (a) balance was insufficient, or
            // (b) ledger insert conflicted (idempotent repeat). In both cases,
            // the caller should treat this as "no ledger entry created".
            if (!newBalance.HasValue)
            {
                ledgerEntry = null;
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return firstUsageDetected;
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
        CancellationToken ct = default)
    {
        if (apiKeyIds.Count == 0)
            return (Array.Empty<ActivityByTokenRecord>(), false, 0);

        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("ApiKeyIds", apiKeyIds.ToArray());
        parameters.Add("Limit", limit + 1);

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

        // T-067: Only run count(*) on first page (no cursor). On write-heavy
        // activity_by_token the full count scan is expensive on every pagination request.
        var isFirstPage = !afterCreatedAtUtc.HasValue;
        var multiSql = ActivityByTokenSelectSql +
            whereClause + "\n" +
            "order by a.created_at_utc desc, a.request_id desc\n" +
            "limit @Limit;\n";
        if (isFirstPage)
        {
            multiSql += $"select count(*) from billing.activity_by_token a {whereClause};";
        }

        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(multiSql, parameters, cancellationToken: ct)).ConfigureAwait(false);

        var items = (await multi.ReadAsync<ActivityByTokenEntity>().ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var totalCount = isFirstPage
            ? await multi.ReadSingleAsync<int>().ConfigureAwait(false)
            : -1;

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
        IReadOnlyList<string>? modelFilters = null,
        IReadOnlyList<string>? providerFilters = null,
        IReadOnlyList<string>? apiKeyFilters = null,
        IReadOnlyList<string>? nodeFilters = null,
        IReadOnlyList<string>? accountFilters = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);

        var conditions = BuildAdminRequestConditions(parameters, afterCursor, modelFilters, providerFilters, apiKeyFilters, nodeFilters, accountFilters, fromUtc, toUtc);
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

    public async Task<(long TotalRequests, long InputTokens, long OutputTokens, long CacheTokens, decimal CostUsd)> GetAdminUsageSummaryAsync(
        IReadOnlyList<string>? modelFilters = null,
        IReadOnlyList<string>? providerFilters = null,
        IReadOnlyList<string>? apiKeyFilters = null,
        IReadOnlyList<string>? nodeFilters = null,
        IReadOnlyList<string>? accountFilters = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();

        var conditions = BuildAdminRequestConditions(parameters, afterCursor: null, modelFilters, providerFilters, apiKeyFilters, nodeFilters, accountFilters, fromUtc, toUtc);
        var whereClause = conditions.Count > 0 ? "where " + string.Join(" and ", conditions) + "\n" : "";

        var result = await connection.QuerySingleAsync<(long TotalRequests, long InputTokens, long OutputTokens, long CacheTokens, decimal CostUsd)>(new CommandDefinition(
            $"""
            select
                count(*) as TotalRequests,
                coalesce(sum(a.input_tokens), 0) as InputTokens,
                coalesce(sum(a.output_tokens), 0) as OutputTokens,
                coalesce(sum(a.cache_tokens), 0) as CacheTokens,
                coalesce(sum(u.cost_amount), 0) as CostUsd
            from billing.activity_by_token a
            left join billing.usage_records u on u.request_id = a.request_id
            {whereClause};
            """,
            parameters,
            cancellationToken: ct)).ConfigureAwait(false);

        return result;
    }

    private static List<string> BuildAdminRequestConditions(
        DynamicParameters parameters,
        string? afterCursor,
        IReadOnlyList<string>? modelFilters,
        IReadOnlyList<string>? providerFilters,
        IReadOnlyList<string>? apiKeyFilters,
        IReadOnlyList<string>? nodeFilters,
        IReadOnlyList<string>? accountFilters,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
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
        if (modelFilters is { Count: > 0 }) { conditions.Add("a.model_id = ANY(@ModelFilters)"); parameters.Add("ModelFilters", modelFilters.ToArray()); }
        if (providerFilters is { Count: > 0 }) { conditions.Add("a.provider_name = ANY(@ProviderFilters)"); parameters.Add("ProviderFilters", providerFilters.ToArray()); }
        if (apiKeyFilters is { Count: > 0 }) { conditions.Add("a.api_key_prefix = ANY(@ApiKeyFilters)"); parameters.Add("ApiKeyFilters", apiKeyFilters.ToArray()); }
        if (nodeFilters is { Count: > 0 }) { conditions.Add("a.router_node_id = ANY(@NodeFilters)"); parameters.Add("NodeFilters", nodeFilters.ToArray()); }
        if (accountFilters is { Count: > 0 }) { conditions.Add("a.provider_account = ANY(@AccountFilters)"); parameters.Add("AccountFilters", accountFilters.ToArray()); }
        if (fromUtc.HasValue) { conditions.Add("a.created_at_utc >= @FromUtc"); parameters.Add("FromUtc", fromUtc.Value); }
        if (toUtc.HasValue) { conditions.Add("a.created_at_utc <= @ToUtc"); parameters.Add("ToUtc", toUtc.Value); }
        return conditions;
    }
}
