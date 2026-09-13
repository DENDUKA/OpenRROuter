using Dapper;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Domain;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

public sealed partial class BillingRepository
{
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
                confirmed_at_utc, canceled_at_utc, bonus_percent, bonus_usd)
            values (
                @Id, @UserId, @ClientToken, @Provider, @ProviderPaymentId, @OrderId,
                @DisplayAmount, @DisplayCurrency, @ProviderAmount, @ProviderCurrency,
                @CreditedUsdAmount, @FxRate, @FxSource, @FxAtUtc, @Status, @CancelReason,
                @LeaseOwner, @LeaseUntilUtc, @CreatedAtUtc, @UpdatedAtUtc, @ExpiresAtUtc,
                @ConfirmedAtUtc, @CanceledAtUtc, @BonusPercent, @BonusUsd)
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
        var npgsqlConn = (Npgsql.NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);
        var outcome = await ConfirmPaymentAtomicAsync(connection, transaction, eventRow, evt, nowUtc, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _topUpsCountCache.Clear();
        return outcome;
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
        var npgsqlConn = (Npgsql.NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);
        var outcome = await ApplyPaymentEventAtomicAsync(
            connection, transaction, eventRow, evt, target.Value, nowUtc, autoReverseChargeback, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _topUpsCountCache.Clear();
        return outcome;
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
        var npgsqlConn = (Npgsql.NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);
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
            """ + PaymentSelectFromUpdatedSql + ";",
            new { WorkerId = workerId, LeaseUntil = now.Add(lease), Now = now, OlderThanUtc = olderThanUtc, Limit = limit },
            transaction, cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain).ToArray();
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return payments;
    }

    public async Task<int> AcquireUnprocessedEventsAsync(int limit, DateTimeOffset nowUtc, bool autoReverseChargeback = false, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var npgsqlConn = (Npgsql.NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);

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

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return events.Length;
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
                payment.CreditedUsdAmount, nowUtc, isUserPayment: true, ct).ConfigureAwait(false);
            await EnqueueYandexMetrikaOfflineConversionAsync(
                connection,
                transaction,
                $"topup_completed:{payment.Id}",
                payment.UserId,
                YandexMetrikaOfflineConversionTargets.TopUpCompleted,
                nowUtc,
                payment.ProviderAmount,
                payment.ProviderCurrency,
                ct).ConfigureAwait(false);
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
            "select coalesce(balance_amount, 0) from billing.user_balances where user_id = @UserId;",
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
                -updated.CreditedUsdAmount, nowUtc, isUserPayment: false, ct).ConfigureAwait(false);

            // Reverse first top-up bonus if it exists
            var bonusKey = $"first_top_up_bonus:{eventRow.PaymentId}";
            var bonusAmount = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                "select amount from billing.ledger_entries where idempotency_key=@Key limit 1;",
                new { Key = bonusKey }, transaction, cancellationToken: ct)).ConfigureAwait(false);
            if (bonusAmount.HasValue && bonusAmount.Value != 0)
            {
                await InsertLedgerAsync(connection, transaction, updated, "refund_bonus",
                    LedgerEntryType.Correction, -bonusAmount.Value, nowUtc, isUserPayment: false, ct).ConfigureAwait(false);
            }
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
        PaymentEntity payment, string prefix, LedgerEntryType type, decimal amount, DateTimeOffset nowUtc, bool isUserPayment, CancellationToken ct)
    {
        var ledgerId = await connection.QuerySingleAsync<string>(new CommandDefinition(
            """
            with ins as (
                insert into billing.ledger_entries (id,user_id,type,amount,currency,idempotency_key,reason,created_at_utc,is_user_payment)
                values (@Id,@UserId,@Type,@Amount,'Usd',@Key,@Reason,@Now,@IsUserPayment)
                on conflict (idempotency_key) do nothing returning id
            )
            select id from ins union all select id from billing.ledger_entries where idempotency_key=@Key limit 1;
            """,
            new
            {
                Id = Guid.NewGuid().ToString("N"), payment.UserId, Type = type.ToString(), Amount = amount,
                Key = $"{prefix}:{payment.Id}", Reason = $"{type} payment {payment.Id}", Now = nowUtc,
                IsUserPayment = isUserPayment
            }, transaction, cancellationToken: ct)).ConfigureAwait(false);

        // T-028: Keep denormalized balance in sync.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.user_balances (user_id, balance_amount, updated_at_utc)
            values (@UserId, @Amount, now())
            on conflict (user_id) do update set
                balance_amount = billing.user_balances.balance_amount + excluded.balance_amount,
                updated_at_utc = now();
            """,
            new { payment.UserId, Amount = amount },
            transaction, cancellationToken: ct)).ConfigureAwait(false);

        return ledgerId;
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
        payment.PayUrl, payment.BonusPercent, payment.BonusUsd
    };

    public async Task<(int TopupsCount, decimal TotalRub, decimal TotalUsd, int ActiveApiUsers)> GetDailyStatsAsync(
        DateTimeOffset sinceUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        const string sql = """
            select
                count(*) as topups_count,
                coalesce(sum(provider_amount), 0) as total_rub,
                coalesce(sum(credited_usd_amount), 0) as total_usd
            from billing.payments
            where status = 'Paid' and confirmed_at_utc >= @SinceUtc;

            select count(distinct user_id)
            from billing.usage_records
            where created_at_utc >= @SinceUtc;
            """;
        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { SinceUtc = sinceUtc }, cancellationToken: ct)).ConfigureAwait(false);
        var row = await multi.ReadSingleAsync<(int TopupsCount, decimal TotalRub, decimal TotalUsd)>().ConfigureAwait(false);
        var activeApiUsers = await multi.ReadSingleAsync<int>().ConfigureAwait(false);
        return (row.TopupsCount, row.TotalRub, row.TotalUsd, activeApiUsers);
    }
}
