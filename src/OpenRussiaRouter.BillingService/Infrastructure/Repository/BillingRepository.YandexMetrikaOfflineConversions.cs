using System.Data;
using Dapper;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

public sealed partial class BillingRepository
{
    public async Task<IReadOnlyCollection<YandexMetrikaOfflineConversion>> AcquireYandexMetrikaOfflineConversionsAsync(
        string workerId,
        TimeSpan lease,
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var npgsqlConnection = (Npgsql.NpgsqlConnection)connection;
        await npgsqlConnection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConnection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var rows = (await connection.QueryAsync<YandexMetrikaOfflineConversionEntity>(new CommandDefinition(
            """
            with candidates as (
                select id
                from billing.yandex_metrika_offline_conversion_outbox
                where status in ('Pending', 'Reconciling', 'Uploaded')
                  and next_attempt_at_utc <= @NowUtc
                  and (lease_owner is null or lease_until_utc < @NowUtc)
                order by next_attempt_at_utc, occurred_at_utc
                limit @Limit
                for update skip locked
            ), updated as (
                update billing.yandex_metrika_offline_conversion_outbox outbox
                set lease_owner = @WorkerId,
                    lease_until_utc = @LeaseUntilUtc,
                    updated_at_utc = @NowUtc
                from candidates
                where outbox.id = candidates.id
                returning outbox.*
            )
            select id as Id, dedup_key as DedupKey, user_id as UserId, target as Target,
                occurred_at_utc as OccurredAtUtc, price as Price, currency as Currency,
                resolved_client_id as ResolvedClientId, status as Status,
                lease_owner as LeaseOwner, lease_until_utc as LeaseUntilUtc,
                attempts as Attempts, next_attempt_at_utc as NextAttemptAtUtc,
                upload_id as UploadId, upload_status as UploadStatus, comment as Comment,
                last_error as LastError, created_at_utc as CreatedAtUtc, updated_at_utc as UpdatedAtUtc
            from updated;
            """,
            new
            {
                WorkerId = workerId,
                LeaseUntilUtc = nowUtc.Add(lease),
                NowUtc = nowUtc,
                Limit = limit
            },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false)).ToArray();

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return rows.Select(ToDomain).ToArray();
    }

    public async Task<string?> SetYandexMetrikaClientIdAsync(
        string id,
        string clientId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            update billing.yandex_metrika_offline_conversion_outbox
            set resolved_client_id = coalesce(resolved_client_id, @ClientId),
                updated_at_utc = @NowUtc
            where id = @Id
            returning resolved_client_id;
            """,
            new { Id = id, ClientId = clientId, NowUtc = nowUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task UpdateYandexMetrikaOfflineConversionAsync(
        string id,
        YandexMetrikaOfflineConversionStatus status,
        DateTimeOffset nextAttemptAtUtc,
        long? uploadId,
        string? uploadStatus,
        string? lastError,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update billing.yandex_metrika_offline_conversion_outbox
            set status = @Status,
                next_attempt_at_utc = @NextAttemptAtUtc,
                upload_id = coalesce(@UploadId, upload_id),
                upload_status = coalesce(@UploadStatus, upload_status),
                last_error = @LastError,
                attempts = attempts + 1,
                lease_owner = null,
                lease_until_utc = null,
                updated_at_utc = @NowUtc
            where id = @Id;
            """,
            new
            {
                Id = id,
                Status = status.ToString(),
                NextAttemptAtUtc = nextAttemptAtUtc,
                UploadId = uploadId,
                UploadStatus = uploadStatus,
                LastError = Truncate(lastError, 512),
                NowUtc = nowUtc
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task EnqueueYandexMetrikaOfflineConversionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string dedupKey,
        string userId,
        string target,
        DateTimeOffset occurredAtUtc,
        decimal? price,
        string? currency,
        CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.yandex_metrika_offline_conversion_outbox (
                id, dedup_key, user_id, target, occurred_at_utc, price, currency,
                status, attempts, next_attempt_at_utc, comment, created_at_utc, updated_at_utc)
            values (
                @Id, @DedupKey, @UserId, @Target, @OccurredAtUtc, @Price, @Currency,
                'Pending', 0, @OccurredAtUtc, @Comment, @OccurredAtUtc, @OccurredAtUtc)
            on conflict (dedup_key) do nothing;
            """,
            new
            {
                Id = id,
                DedupKey = dedupKey,
                UserId = userId,
                Target = target,
                OccurredAtUtc = occurredAtUtc,
                Price = price,
                Currency = currency,
                Comment = $"orr:{id}"
            },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static YandexMetrikaOfflineConversion ToDomain(YandexMetrikaOfflineConversionEntity entity) =>
        new(
            entity.Id,
            entity.DedupKey,
            entity.UserId,
            entity.Target,
            entity.OccurredAtUtc,
            entity.Price,
            entity.Currency,
            entity.ResolvedClientId,
            Enum.Parse<YandexMetrikaOfflineConversionStatus>(entity.Status, ignoreCase: false),
            entity.LeaseOwner,
            entity.LeaseUntilUtc,
            entity.Attempts,
            entity.NextAttemptAtUtc,
            entity.UploadId,
            entity.UploadStatus,
            entity.Comment,
            entity.LastError,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

    private static string? Truncate(string? value, int maximumLength) =>
        value is { Length: > 0 } && value.Length > maximumLength
            ? value[..maximumLength]
            : value;
}
