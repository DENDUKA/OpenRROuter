using Dapper;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Repository;

public sealed class ControlPlaneRepository(IControlPlaneDbConnectionFactory connectionFactory) : IControlPlaneRepository
{
    public async Task UpsertNodeAsync(RouterNode node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into control.router_nodes
                (id, name, endpoint_url, control_token, capacity_weight, enabled, status,
                 in_flight_requests, last_checked_at_utc, last_health_latency_ms,
                 last_health_error_code, created_at_utc)
            values
                (@Id, @Name, @EndpointUrl, @ControlToken, @CapacityWeight, @Enabled, @Status,
                 @InFlightRequests, @LastCheckedAtUtc, @LastHealthLatencyMs,
                 @LastHealthErrorCode, @CreatedAtUtc)
            on conflict (id) do update set
                name = excluded.name,
                endpoint_url = excluded.endpoint_url,
                control_token = excluded.control_token,
                capacity_weight = excluded.capacity_weight,
                enabled = excluded.enabled,
                status = excluded.status,
                last_checked_at_utc = excluded.last_checked_at_utc,
                last_health_latency_ms = excluded.last_health_latency_ms,
                last_health_error_code = excluded.last_health_error_code;
            """,
            ToParameters(node),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        // Delete leases first: FK has no ON DELETE CASCADE (migration 001).
        // Once the migration to add CASCADE is applied, this explicit delete becomes
        // a harmless no-op but remains safe to keep for clarity.
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from control.router_leases where node_id = @NodeId;",
            new { NodeId = nodeId },
            cancellationToken: ct)).ConfigureAwait(false);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "delete from control.router_nodes where id = @NodeId;",
            new { NodeId = nodeId },
            cancellationToken: ct)).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task<RouterNode?> GetNodeAsync(string nodeId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<RouterNodeEntity>(new CommandDefinition(
                NodeByIdSql, new { NodeId = nodeId }, cancellationToken: ct)).ConfigureAwait(false) is { } entity
            ? ControlPlanePersistenceMapper.ToDomain(entity)
            : null;
    }

    public async Task<(IReadOnlyCollection<RouterNode> Items, bool HasMore)> ListNodesAsync(int limit, string? afterName = null, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterName != null)
        {
            afterCondition = "where name > @AfterName\n";
            parameters.Add("AfterName", afterName);
        }

        var items = (await connection.QueryAsync<RouterNodeEntity>(new CommandDefinition(
                NodeSelectSql + afterCondition + "order by name, id\nlimit @Limit;",
                parameters, cancellationToken: ct)).ConfigureAwait(false))
            .Select(ControlPlanePersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<RouterNodeLease?> TryAcquireLeaseAsync(
        IReadOnlyCollection<string> candidateNodeIds,
        RouterLease lease,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (candidateNodeIds.Count == 0)
            return null;

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var node = await connection.QuerySingleOrDefaultAsync<LeaseCandidate>(new CommandDefinition(
            """
            with candidate as (
                select id, endpoint_url, control_token
                from control.router_nodes
                where id = any(@CandidateNodeIds)
                  and enabled = true
                  and status = 'Healthy'
                order by in_flight_requests::numeric / capacity_weight, in_flight_requests, name
                for update skip locked
                limit 1
            ), leased_node as (
                update control.router_nodes node
                set in_flight_requests = node.in_flight_requests + 1
                from candidate
                where node.id = candidate.id
                returning node.id as Id, candidate.endpoint_url as EndpointUrl, candidate.control_token as ControlToken
            )
            select * from leased_node;
            """,
            new { CandidateNodeIds = candidateNodeIds.ToArray() }, transaction, cancellationToken: ct)).ConfigureAwait(false);

        if (node is null)
        {
            transaction.Commit();
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into control.router_leases
                (id, node_id, request_id, expires_at_utc, created_at_utc)
            values
                (@Id, @NodeId, @RequestId, @ExpiresAtUtc, @CreatedAtUtc);
            """,
            lease with { NodeId = node.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        transaction.Commit();
        return new RouterNodeLease(lease.Id, node.Id, new Uri(node.EndpointUrl), node.ControlToken);
    }

    public async Task<bool> ReleaseLeaseAsync(string leaseId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            with released as (
                delete from control.router_leases
                where id = @LeaseId
                returning node_id
            ), decremented as (
                update control.router_nodes node
                set in_flight_requests = greatest(node.in_flight_requests - 1, 0)
                from released
                where node.id = released.node_id
                returning node.id
            )
            select exists(select 1 from released);
            """,
            new { LeaseId = leaseId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> ReleaseExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            with expired as (
                delete from control.router_leases
                where expires_at_utc <= @NowUtc
                returning node_id
            ), counts as (
                select node_id, count(*)::int as lease_count
                from expired
                group by node_id
            ), decremented as (
                update control.router_nodes node
                set in_flight_requests = greatest(node.in_flight_requests - counts.lease_count, 0)
                from counts
                where node.id = counts.node_id
                returning node.id
            )
            select count(*)::int from expired;
            """,
            new { NowUtc = nowUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> ApplyHealthAsync(
        string nodeId,
        RouterNodeStatus status,
        DateTimeOffset checkedAtUtc,
        int? latencyMs,
        string? errorCode,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            update control.router_nodes
            set status = case
                    when enabled = false then 'Disabled'
                    when @Status = 'Disabled' then 'Unhealthy'
                    else @Status
                end,
                last_checked_at_utc = @CheckedAtUtc,
                last_health_latency_ms = @LatencyMs,
                last_health_error_code = @ErrorCode
            where id = @NodeId;
            """,
            new
            {
                NodeId = nodeId,
                Status = status.ToString(),
                CheckedAtUtc = checkedAtUtc,
                LatencyMs = latencyMs,
                ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim()
            },
            cancellationToken: ct)).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task SaveIntegrationKeyMappingAsync(string apiKeyId, string nodeId, string routerKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into control.integration_key_sync (api_key_id, node_id, router_key_id)
            values (@ApiKeyId, @NodeId, @RouterKeyId)
            on conflict (api_key_id, node_id) do update set
                router_key_id = excluded.router_key_id,
                pushed_at_utc = now();
            """,
            new { ApiKeyId = apiKeyId, NodeId = nodeId, RouterKeyId = routerKeyId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(string NodeId, string RouterKeyId)>> GetIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<(string node_id, string router_key_id)>(new CommandDefinition(
                """
                select node_id, router_key_id
                from control.integration_key_sync
                where api_key_id = @ApiKeyId;
                """,
                new { ApiKeyId = apiKeyId },
                cancellationToken: ct)).ConfigureAwait(false))
            .Select(r => (r.node_id, r.router_key_id))
            .ToList();
    }

    public async Task RemoveIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from control.integration_key_sync where api_key_id = @ApiKeyId;",
            new { ApiKeyId = apiKeyId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static object ToParameters(RouterNode node) => new
    {
        node.Id,
        node.Name,
        EndpointUrl = node.EndpointUrl.ToString(),
        node.ControlToken,
        node.CapacityWeight,
        node.Enabled,
        Status = node.Status.ToString(),
        node.InFlightRequests,
        node.LastCheckedAtUtc,
        node.LastHealthLatencyMs,
        node.LastHealthErrorCode,
        node.CreatedAtUtc
    };

    private const string NodeSelectSql = """
        select
            id as Id,
            name as Name,
            endpoint_url as EndpointUrl,
            control_token as ControlToken,
            capacity_weight as CapacityWeight,
            enabled as Enabled,
            status as Status,
            in_flight_requests as InFlightRequests,
            last_checked_at_utc as LastCheckedAtUtc,
            last_health_latency_ms as LastHealthLatencyMs,
            last_health_error_code as LastHealthErrorCode,
            created_at_utc as CreatedAtUtc
        from control.router_nodes

        """;

    private const string NodeByIdSql = NodeSelectSql + "\nwhere id = @NodeId;";

    private sealed record LeaseCandidate(string Id, string EndpointUrl, string ControlToken);
}
