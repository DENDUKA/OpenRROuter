using Dapper;
using FluentAssertions;
using Npgsql;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application.Abstractions;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;

namespace OpenRussiaRouter.ControlPlane.Tests.Infrastructure.Persistence;

public sealed class ControlPlaneRepositoryPostgresTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Store_AppliedMigration_PersistsNodeLeaseAndCleanup()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        var connectionString = database.ConnectionString;
        database.ApplyMigration();
        using var connection = new NpgsqlConnection(connectionString);
        database.Truncate();
        var service = new RouterControlPlaneService(
            new ControlPlaneRepository(new ControlPlaneDbConnectionFactory(connectionString)),
            new NodeModelCapabilityStore());
        var now = DateTimeOffset.UtcNow;
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, now, 15, null);

        var lease = await service.SelectRouterNodeAsync(null, "req-1", now);
        await service.ReleaseExpiredLeasesAsync(now.AddMinutes(6));

        lease.NodeId.Should().Be(node.Id);
        var summary = (await service.ListRouterNodesAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(0);
        summary.Status.Should().Be(RouterNodeStatus.Healthy);
    }

}
