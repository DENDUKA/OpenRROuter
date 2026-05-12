using Dapper;
using OpenRussiaRouter.ControlPlane.Application.Interfaces;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

/// <summary>
/// Dapper/PostgreSQL-backed Control Plane repository.
/// </summary>
public sealed class DapperControlPlaneRepository(IControlPlaneDbConnectionFactory connectionFactory) : IControlPlaneRepository
{
    /// <inheritdoc />
    public void UpsertNode(RouterNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        using var connection = connectionFactory.CreateConnection();
        connection.Execute(
            """
            insert into control.router_nodes
                (id, name, endpoint_url, token_secret_name, capacity_weight, enabled, status,
                 in_flight_requests, last_checked_at_utc, last_health_latency_ms,
                 last_health_error_code, created_at_utc)
            values
                (@Id, @Name, @EndpointUrl, @TokenSecretName, @CapacityWeight, @Enabled, @Status,
                 @InFlightRequests, @LastCheckedAtUtc, @LastHealthLatencyMs,
                 @LastHealthErrorCode, @CreatedAtUtc)
            on conflict (id) do update set
                name = excluded.name,
                endpoint_url = excluded.endpoint_url,
                token_secret_name = excluded.token_secret_name,
                capacity_weight = excluded.capacity_weight,
                enabled = excluded.enabled,
                status = excluded.status,
                in_flight_requests = excluded.in_flight_requests,
                last_checked_at_utc = excluded.last_checked_at_utc,
                last_health_latency_ms = excluded.last_health_latency_ms,
                last_health_error_code = excluded.last_health_error_code;
            """,
            ToParameters(node));
    }

    /// <inheritdoc />
    public RouterNode? GetNode(string nodeId)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<RouterNodeEntity>(NodeByIdSql, new { NodeId = nodeId }) is { } entity
            ? ControlPlanePersistenceMapper.ToDomain(entity)
            : null;
    }

    /// <inheritdoc />
    public (IReadOnlyCollection<RouterNode> Items, bool HasMore) ListNodes(int limit, string? afterName = null)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = connection.Query<RouterNodeEntity>(
                NodeSelectSql + """
                 where @AfterName is null or name > @AfterName
                 order by name
                 limit @Limit;
                 """,
                new { Limit = limit + 1, AfterName = afterName })
            .Select(ControlPlanePersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore)
        {
            items = items.Take(limit).ToArray();
        }

        return (items, hasMore);
    }

    /// <inheritdoc />
    public void AddLease(RouterLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        using var connection = connectionFactory.CreateConnection();
        connection.Execute(
            """
            insert into control.router_leases
                (id, node_id, request_id, expires_at_utc, created_at_utc)
            values
                (@Id, @NodeId, @RequestId, @ExpiresAtUtc, @CreatedAtUtc);
            """,
            lease);
    }

    /// <inheritdoc />
    public RouterLease? RemoveLease(string leaseId)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.QuerySingleOrDefault<RouterLeaseEntity>(
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
            new { LeaseId = leaseId }) is { } entity
                ? ControlPlanePersistenceMapper.ToDomain(entity)
                : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<RouterLease> RemoveExpiredLeases(DateTimeOffset nowUtc)
    {
        using var connection = connectionFactory.CreateConnection();
        return connection.Query<RouterLeaseEntity>(
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
                new { NowUtc = nowUtc })
            .Select(ControlPlanePersistenceMapper.ToDomain)
            .ToArray();
    }

    private static object ToParameters(RouterNode node)
    {
        return new
        {
            node.Id,
            node.Name,
            EndpointUrl = node.EndpointUrl.ToString(),
            node.TokenSecretName,
            node.CapacityWeight,
            node.Enabled,
            Status = node.Status.ToString(),
            node.InFlightRequests,
            node.LastCheckedAtUtc,
            node.LastHealthLatencyMs,
            node.LastHealthErrorCode,
            node.CreatedAtUtc
        };
    }

    private const string NodeSelectSql = """
        select
            id as Id,
            name as Name,
            endpoint_url as EndpointUrl,
            token_secret_name as TokenSecretName,
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

    private const string NodeByIdSql = NodeSelectSql + " where id = @NodeId;";
}
