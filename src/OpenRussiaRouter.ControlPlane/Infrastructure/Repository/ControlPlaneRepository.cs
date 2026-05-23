using Dapper;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Repository;

public sealed class ControlPlaneRepository(IControlPlaneDbConnectionFactory connectionFactory) : IControlPlaneRepository
{
    public async Task UpsertNodeAsync(RouterNode node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
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
                in_flight_requests = excluded.in_flight_requests,
                last_checked_at_utc = excluded.last_checked_at_utc,
                last_health_latency_ms = excluded.last_health_latency_ms,
                last_health_error_code = excluded.last_health_error_code;
            """,
            ToParameters(node)).ConfigureAwait(false);
    }

    public async Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        // leases have FK with ON DELETE CASCADE, so deleting the node removes them too
        var affected = await connection.ExecuteAsync(
            "delete from control.router_nodes where id = @NodeId;",
            new { NodeId = nodeId }).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task<RouterNode?> GetNodeAsync(string nodeId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<RouterNodeEntity>(NodeByIdSql, new { NodeId = nodeId }).ConfigureAwait(false) is { } entity
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

        var items = (await connection.QueryAsync<RouterNodeEntity>(
                NodeSelectSql + afterCondition + "order by name\nlimit @Limit;",
                parameters).ConfigureAwait(false))
            .Select(ControlPlanePersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task AddLeaseAsync(RouterLease lease, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            insert into control.router_leases
                (id, node_id, request_id, expires_at_utc, created_at_utc)
            values
                (@Id, @NodeId, @RequestId, @ExpiresAtUtc, @CreatedAtUtc);
            """,
            lease).ConfigureAwait(false);
    }

    public async Task<RouterLease?> RemoveLeaseAsync(string leaseId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<RouterLeaseEntity>(
            """
            delete from control.router_leases
            where id = @LeaseId
            returning
                id as Id,
                node_id as NodeId,
                request_id as RequestId,
                expires_at_utc as ExpiresAtUtc,
                created_at_utc as CreatedAtUtc;
            """,
            new { LeaseId = leaseId }).ConfigureAwait(false) is { } entity
                ? ControlPlanePersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<IReadOnlyCollection<RouterLease>> RemoveExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<RouterLeaseEntity>(
                """
                delete from control.router_leases
                where expires_at_utc <= @NowUtc
                returning
                    id as Id,
                    node_id as NodeId,
                    request_id as RequestId,
                    expires_at_utc as ExpiresAtUtc,
                    created_at_utc as CreatedAtUtc;
                """,
                new { NowUtc = nowUtc }).ConfigureAwait(false))
            .Select(ControlPlanePersistenceMapper.ToDomain)
            .ToArray();
    }

    public async Task ReleaseNodesAsync(IReadOnlyCollection<string> nodeIds, CancellationToken ct = default)
    {
        if (nodeIds.Count == 0)
            return;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            update control.router_nodes
            set in_flight_requests = greatest(in_flight_requests - 1, 0)
            where id = any(@NodeIds);
            """,
            new { NodeIds = nodeIds.ToArray() }).ConfigureAwait(false);
    }

    public async Task SaveIntegrationKeyMappingAsync(string apiKeyId, string nodeId, string routerKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            insert into control.integration_key_sync (api_key_id, node_id, router_key_id)
            values (@ApiKeyId, @NodeId, @RouterKeyId)
            on conflict (api_key_id, node_id) do update set
                router_key_id = excluded.router_key_id,
                pushed_at_utc = now();
            """,
            new { ApiKeyId = apiKeyId, NodeId = nodeId, RouterKeyId = routerKeyId }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(string NodeId, string RouterKeyId)>> GetIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<(string node_id, string router_key_id)>(
                """
                select node_id, router_key_id
                from control.integration_key_sync
                where api_key_id = @ApiKeyId;
                """,
                new { ApiKeyId = apiKeyId }).ConfigureAwait(false))
            .Select(r => (r.node_id, r.router_key_id))
            .ToList();
    }

    public async Task RemoveIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "delete from control.integration_key_sync where api_key_id = @ApiKeyId;",
            new { ApiKeyId = apiKeyId }).ConfigureAwait(false);
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
}
