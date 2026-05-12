using Dapper;
using Npgsql;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application;
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
            new ControlPlaneRepository(new ControlPlaneDbConnectionFactory(connectionString)));
        var now = DateTimeOffset.UtcNow;
        var node = service.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        service.ApplyHealth(node.Id, RouterNodeStatus.Healthy, now, 15, null);

        var lease = service.SelectRouterNode("req-1", now);
        service.ReleaseExpiredLeases(now.AddMinutes(6));

        Assert.Equal(node.Id, lease.NodeId);
        var summary = Assert.Single(service.ListRouterNodes());
        Assert.Equal(0, summary.InFlightRequests);
        Assert.Equal(RouterNodeStatus.Healthy, summary.Status);
    }

}
