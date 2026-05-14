using System.Data;
using System.Text.Json;
using Dapper;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.Infrastructure.Persistence;

namespace OpenRussiaRouter.ApiGateway.Infrastructure.Repository;

public sealed class DapperUsageOutboxRepository(IGatewayDbConnectionFactory connectionFactory) : IUsageOutboxRepository
{
    public async Task EnqueueAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usageEvent);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var nowUtc = DateTimeOffset.UtcNow;

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into gateway.usage_outbox
                (request_id, payload_json, status, attempts, next_attempt_at_utc,
                 locked_by, locked_until_utc, last_error, created_at_utc, updated_at_utc)
            values
                (@RequestId, @PayloadJson, 'pending', 0, @NowUtc,
                 null, null, null, @NowUtc, @NowUtc)
            on conflict (request_id) do nothing;
            """,
            new
            {
                usageEvent.RequestId,
                PayloadJson = JsonSerializer.Serialize(usageEvent),
                NowUtc = nowUtc
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<UsageOutboxItem>> LeaseDueAsync(
        int batchSize,
        string workerId,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var requestIds = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            select request_id
            from gateway.usage_outbox
            where (status = 'pending' and next_attempt_at_utc <= @NowUtc)
               or (status = 'leased' and locked_until_utc <= @NowUtc)
            order by next_attempt_at_utc
            limit @BatchSize
            for update skip locked;
            """,
            new { BatchSize = batchSize, NowUtc = nowUtc },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        if (requestIds.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return [];
        }

        var rows = (await connection.QueryAsync<UsageOutboxRow>(new CommandDefinition(
            """
            update gateway.usage_outbox
            set status = 'leased',
                locked_by = @WorkerId,
                locked_until_utc = @LockedUntilUtc,
                updated_at_utc = @NowUtc
            where request_id = any(@RequestIds)
            returning
                request_id as RequestId,
                payload_json as PayloadJson,
                attempts as Attempts,
                next_attempt_at_utc as NextAttemptAtUtc;
            """,
            new
            {
                WorkerId = workerId,
                LockedUntilUtc = nowUtc.Add(leaseDuration),
                NowUtc = nowUtc,
                RequestIds = requestIds
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(ToItem).ToArray();
    }

    public async Task MarkDeliveredAsync(string requestId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            update gateway.usage_outbox
            set status = 'delivered',
                locked_by = null,
                locked_until_utc = null,
                last_error = null,
                updated_at_utc = @NowUtc
            where request_id = @RequestId;
            """,
            new { RequestId = requestId, NowUtc = nowUtc },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(
        string requestId,
        int attempts,
        DateTimeOffset nextAttemptAtUtc,
        string lastError,
        bool dead,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            update gateway.usage_outbox
            set status = @Status,
                attempts = @Attempts,
                next_attempt_at_utc = @NextAttemptAtUtc,
                locked_by = null,
                locked_until_utc = null,
                last_error = @LastError,
                updated_at_utc = @NowUtc
            where request_id = @RequestId;
            """,
            new
            {
                RequestId = requestId,
                Status = dead ? "dead" : "pending",
                Attempts = attempts,
                NextAttemptAtUtc = nextAttemptAtUtc,
                LastError = lastError,
                NowUtc = nowUtc
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static UsageOutboxItem ToItem(UsageOutboxRow row)
    {
        var usageEvent = JsonSerializer.Deserialize<GatewayUsageEvent>(row.PayloadJson)
            ?? throw new InvalidOperationException($"Usage outbox payload could not be deserialized. RequestId: {row.RequestId}");

        return new UsageOutboxItem(usageEvent, row.Attempts, row.NextAttemptAtUtc);
    }

    private sealed record UsageOutboxRow(
        string RequestId,
        string PayloadJson,
        int Attempts,
        DateTimeOffset NextAttemptAtUtc);
}
