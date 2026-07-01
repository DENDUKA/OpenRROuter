using Dapper;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed class PostgresUsageOutboxRepository(IGatewayDbConnectionFactory connectionFactory) : IUsageOutboxRepository
{
    public async Task EnqueueAsync(GatewayUsageEvent ev, CancellationToken ct)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into gateway.usage_outbox
                (request_id, user_id, api_key_id, api_key_prefix, model_id, router_node_id,
                 input_tokens, output_tokens, cache_tokens, status,
                 started_at_utc, completed_at_utc,
                 provider_name, provider_account, endpoint, stream,
                 attempts, next_attempt_at_utc, leased_until_utc, created_at_utc)
            values
                (@RequestId, @UserId, @ApiKeyId, @ApiKeyPrefix, @ModelId, @RouterNodeId,
                 @InputTokens, @OutputTokens, @CacheTokens, @Status,
                 @StartedAtUtc, @CompletedAtUtc,
                 @ProviderName, @ProviderAccount, @Endpoint, @Stream,
                 0, now(), null, now())
            on conflict (request_id) do nothing;
            """,
            new
            {
                ev.RequestId,
                ev.UserId,
                ev.ApiKeyId,
                ev.ApiKeyPrefix,
                ev.ModelId,
                ev.RouterNodeId,
                ev.Usage.InputTokens,
                ev.Usage.OutputTokens,
                ev.Usage.CacheTokens,
                ev.Status,
                ev.StartedAtUtc,
                ev.CompletedAtUtc,
                ProviderName = ev.ProviderName ?? (object)DBNull.Value,
                ProviderAccount = ev.ProviderAccount ?? (object)DBNull.Value,
                Endpoint = ev.Endpoint ?? (object)DBNull.Value,
                Stream = ev.Stream ?? false
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UsageOutboxRecord>> ClaimPendingAsync(
        int batchSize,
        TimeSpan lease,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var leasedUntil = nowUtc.Add(lease);

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var rows = (await connection.QueryAsync<UsageOutboxRow>(new CommandDefinition(
            """
            with updated as (
                update gateway.usage_outbox
                set leased_until_utc = @LeasedUntil
                where ctid in (
                    select ctid from gateway.usage_outbox
                    where next_attempt_at_utc <= @Now
                      and (leased_until_utc is null or leased_until_utc < @Now)
                    order by next_attempt_at_utc
                    limit @BatchSize
                    for update skip locked
                )
                returning *
            )
            select
                request_id as RequestId,
                user_id as UserId,
                api_key_id as ApiKeyId,
                api_key_prefix as ApiKeyPrefix,
                model_id as ModelId,
                router_node_id as RouterNodeId,
                input_tokens as InputTokens,
                output_tokens as OutputTokens,
                cache_tokens as CacheTokens,
                status as Status,
                started_at_utc as StartedAtUtc,
                completed_at_utc as CompletedAtUtc,
                provider_name as ProviderName,
                provider_account as ProviderAccount,
                endpoint as Endpoint,
                stream as Stream,
                attempts as Attempts,
                next_attempt_at_utc as NextAttemptAtUtc,
                created_at_utc as CreatedAtUtc,
                leased_until_utc as LeasedUntilUtc
            from updated;
            """,
            new
            {
                LeasedUntil = leasedUntil,
                Now = nowUtc,
                BatchSize = batchSize
            },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false))
            .Select(static row => row.ToRecord())
            .ToArray();

        transaction.Commit();
        return rows;
    }

    public async Task MarkSentAsync(string requestId, CancellationToken ct)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from gateway.usage_outbox where request_id = @RequestId;",
            new { RequestId = requestId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(
        string requestId,
        int attempts,
        DateTimeOffset nextAttemptUtc,
        CancellationToken ct)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update gateway.usage_outbox
            set attempts = @Attempts,
                next_attempt_at_utc = @NextAttemptAtUtc,
                leased_until_utc = null
            where request_id = @RequestId;
            """,
            new
            {
                RequestId = requestId,
                Attempts = attempts,
                NextAttemptAtUtc = nextAttemptUtc
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    // Dapper materializes flat columns; it cannot populate the nested TokenUsage of
    // UsageOutboxRecord directly. Mirror the BillingRepository/BillingPersistenceMapper
    // pattern: read into a flat row, then build the record (and its TokenUsage) by hand.
    private sealed class UsageOutboxRow
    {
        public string RequestId { get; init; } = string.Empty;
        public string UserId { get; init; } = string.Empty;
        public string ApiKeyId { get; init; } = string.Empty;
        public string ApiKeyPrefix { get; init; } = string.Empty;
        public string ModelId { get; init; } = string.Empty;
        public string RouterNodeId { get; init; } = string.Empty;
        public long InputTokens { get; init; }
        public long OutputTokens { get; init; }
        public long CacheTokens { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset StartedAtUtc { get; init; }
        public DateTimeOffset CompletedAtUtc { get; init; }
        public string? ProviderName { get; init; }
        public string? ProviderAccount { get; init; }
        public string? Endpoint { get; init; }
        public bool Stream { get; init; }
        public int Attempts { get; init; }
        public DateTimeOffset NextAttemptAtUtc { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? LeasedUntilUtc { get; init; }

        public UsageOutboxRecord ToRecord()
        {
            return new UsageOutboxRecord(
                RequestId,
                UserId,
                ApiKeyId,
                ApiKeyPrefix,
                ModelId,
                RouterNodeId,
                new TokenUsage(InputTokens, OutputTokens, CacheTokens),
                Status,
                StartedAtUtc,
                CompletedAtUtc,
                ProviderName,
                ProviderAccount,
                Endpoint,
                Stream,
                Attempts,
                NextAttemptAtUtc,
                CreatedAtUtc,
                LeasedUntilUtc);
        }
    }
}
