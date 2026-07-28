using Dapper;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BillingService.Domain.Enums;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

public sealed partial class BillingRepository
{
    public async Task<(IReadOnlyCollection<LedgerEntry> Items, bool HasMore)> ListTopUpsAsync(string userId, int limit, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<LedgerEntryEntity>(new CommandDefinition(
                LedgerEntrySelectSql +
                """
                where user_id = @UserId and type in ('VirtualTopUp','ExternalTopUp','Promo1')
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

    public async Task<(IReadOnlyCollection<LedgerEntry> Items, int TotalCount)> ListAllTopUpsAsync(
        int limit,
        IReadOnlyList<string>? userIds = null,
        decimal? minAmountUsd = null,
        decimal? maxAmountUsd = null,
        DateTimeOffset? afterCreatedAtUtc = null,
        string? afterId = null,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();

        const string baseWhere = """
            where type in ('VirtualTopUp','ExternalTopUp','Promo1')
              and (@UserIds::text[] is null or user_id = any(@UserIds::text[]))
              and (@MinAmountUsd::numeric is null or amount >= @MinAmountUsd::numeric)
              and (@MaxAmountUsd::numeric is null or amount <= @MaxAmountUsd::numeric)
            """;

        var userFilter = userIds is null ? "*" : string.Join(",", userIds.OrderBy(id => id, StringComparer.Ordinal));
        var cacheKey = $"topups:{userFilter}:{minAmountUsd}:{maxAmountUsd}";
        var now = DateTimeOffset.UtcNow;
        var totalCount = 0;

        if (_topUpsCountCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > now)
        {
            totalCount = cached.Count;
        }
        else
        {
            var countSql = $"select count(*) from billing.ledger_entries {baseWhere};";
            totalCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                countSql,
                new { UserIds = userIds, MinAmountUsd = minAmountUsd, MaxAmountUsd = maxAmountUsd },
                cancellationToken: ct)).ConfigureAwait(false);
            _topUpsCountCache[cacheKey] = (totalCount, now + TopUpsCountCacheTtl);
        }

        var keysetClause = afterCreatedAtUtc is not null && afterId is not null
            ? " and (created_at_utc, id) < (@AfterCreatedAtUtc, @AfterId)"
            : "";

        var dataSql = LedgerEntrySelectSql + baseWhere + keysetClause +
            " order by created_at_utc desc, id desc limit @Limit;";

        var items = (await connection.QueryAsync<LedgerEntryEntity>(new CommandDefinition(
                dataSql,
                new
                {
                    UserIds = userIds,
                    MinAmountUsd = minAmountUsd,
                    MaxAmountUsd = maxAmountUsd,
                    AfterCreatedAtUtc = afterCreatedAtUtc,
                    AfterId = afterId,
                    Limit = limit
                },
                cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        return (items, totalCount);
    }

    public async Task<LedgerEntry> AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var connection = connectionFactory.CreateConnection();
        var result = BillingPersistenceMapper.ToDomain(await connection.QueryFirstAsync<LedgerEntryEntity>(new CommandDefinition(
            """
            with ins as (
                insert into billing.ledger_entries
                    (id, user_id, type, amount, currency, idempotency_key, reason, created_at_utc, is_user_payment)
                values
                    (@Id, @UserId, @Type, @Amount, @Currency, @IdempotencyKey, @Reason, @CreatedAtUtc, @IsUserPayment)
                on conflict (idempotency_key) do nothing
                returning *
            ),
            bal as (
                insert into billing.user_balances (user_id, balance_amount, updated_at_utc)
                select ins.user_id, ins.amount, now() from ins
                on conflict (user_id) do update set
                    balance_amount = billing.user_balances.balance_amount + excluded.balance_amount,
                    updated_at_utc = now()
            )
            select id as Id, user_id as UserId, type as Type, amount as Amount, currency as Currency,
                   idempotency_key as IdempotencyKey, reason as Reason, created_at_utc as CreatedAtUtc,
                   is_user_payment as IsUserPayment
            from ins
            union all
            select id as Id, user_id as UserId, type as Type, amount as Amount, currency as Currency,
                   idempotency_key as IdempotencyKey, reason as Reason, created_at_utc as CreatedAtUtc,
                   is_user_payment as IsUserPayment
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
                entry.CreatedAtUtc,
                entry.IsUserPayment
            },
            cancellationToken: ct)).ConfigureAwait(false));

        _topUpsCountCache.Clear();
        return result;
    }

    public async Task<MoneyAmount> GetBalanceAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var amount = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "select coalesce(balance_amount, 0) from billing.user_balances where user_id = @UserId;",
            new { UserId = userId },
            cancellationToken: ct)).ConfigureAwait(false);
        return MoneyAmount.Usd(amount);
    }

    public async Task<decimal?> TryDebitBalanceAsync(string userId, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Debit amount must be positive.");

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            """
            update billing.user_balances
            set balance_amount = balance_amount - @Amount, updated_at_utc = now()
            where user_id = @UserId and balance_amount >= @Amount
            returning balance_amount;
            """,
            new { UserId = userId, Amount = amount },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task AddBalanceAsync(string userId, decimal amount, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.user_balances (user_id, balance_amount, updated_at_utc)
            values (@UserId, @Amount, now())
            on conflict (user_id) do update set
                balance_amount = billing.user_balances.balance_amount + excluded.balance_amount,
                updated_at_utc = now();
            """,
            new { UserId = userId, Amount = amount },
            cancellationToken: ct)).ConfigureAwait(false);
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

    public async Task<string?> GetPlatformSettingAsync(string key, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select value from billing.platform_settings where key = @Key;",
            new { Key = key },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPlatformSettingsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<(string Key, string Value)>(new CommandDefinition(
            "select key, value from billing.platform_settings where key = any(@Keys);",
            new { Keys = keys.ToArray() },
            cancellationToken: ct)).ConfigureAwait(false))
            .ToArray();
        var result = new Dictionary<string, string>(rows.Length, StringComparer.Ordinal);
        foreach (var (key, value) in rows)
            result[key] = value;
        return result;
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

    public async Task<string?> HasPromo1EntryAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select id from billing.ledger_entries where user_id = @UserId and idempotency_key = @Key limit 1;",
            new { UserId = userId, Key = $"promo1:{userId}" },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> HasUserHadPaidTopUpAsync(string userId, string? excludePaymentId = null, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1 from billing.ledger_entries
                where user_id = @UserId and is_user_payment = true
                  and (@ExcludePaymentId::text is null or idempotency_key <> 'payment:' || @ExcludePaymentId::text)
                limit 1
            );
            """,
            new { UserId = userId, ExcludePaymentId = excludePaymentId },
            cancellationToken: ct)).ConfigureAwait(false);
        return exists;
    }

    public async Task<bool> HasFirstTopUpBonusEntryAsync(string paymentId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select id from billing.ledger_entries where idempotency_key = @Key limit 1;",
            new { Key = $"first_top_up_bonus:{paymentId}" },
            cancellationToken: ct)).ConfigureAwait(false);
        return id is not null;
    }

    public async Task UpdatePaymentBonusAsync(string paymentId, decimal bonusPercent, decimal bonusUsd, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "update billing.payments set bonus_percent = @Percent, bonus_usd = @Usd, updated_at_utc = now() where id = @Id;",
            new { Id = paymentId, Percent = bonusPercent, Usd = bonusUsd },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<(string? LedgerEntryId, decimal Amount)?> GetFirstTopUpBonusForPaymentAsync(string paymentId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<(string Id, decimal Amount)?>(new CommandDefinition(
            "select id, amount from billing.ledger_entries where idempotency_key = @Key limit 1;",
            new { Key = $"first_top_up_bonus:{paymentId}" },
            cancellationToken: ct)).ConfigureAwait(false);
        return row.HasValue ? (row.Value.Id, row.Value.Amount) : null;
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
}
