using Dapper;
using FluentAssertions;
using Npgsql;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain;
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
        var summary = (await service.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(0);
        summary.Status.Should().Be(RouterNodeStatus.Healthy);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Store_ConcurrentLeasesAndHealth_KeepEverySuccessfulIncrement()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
            return;

        database.ApplyMigration();
        database.Truncate();
        var service = new RouterControlPlaneService(
            new ControlPlaneRepository(new ControlPlaneDbConnectionFactory(database.ConnectionString)),
            new NodeModelCapabilityStore());
        var now = DateTimeOffset.UtcNow;
        var nodeA = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        var nodeB = await service.RegisterNodeAsync("node-b", new Uri("https://router-b.example"), "secret-b", 1, now);
        await service.ApplyHealthAsync(nodeA.Id, RouterNodeStatus.Healthy, now);
        await service.ApplyHealthAsync(nodeB.Id, RouterNodeStatus.Healthy, now);

        var operations = Enumerable.Range(0, 32).Select(async index =>
        {
            var health = service.ApplyHealthAsync(index % 2 == 0 ? nodeA.Id : nodeB.Id, RouterNodeStatus.Healthy, now);
            try
            {
                await service.SelectRouterNodeAsync(null, $"request-{index}", now);
                await health;
                return 1;
            }
            catch (InvalidOperationException)
            {
                await health;
                return 0;
            }
        });

        var acquired = (await Task.WhenAll(operations)).Sum();

        var nodes = await service.ListNodesForHealthChecksAsync();
        nodes.Sum(node => node.InFlightRequests).Should().Be(acquired);
        nodes.Should().OnlyContain(node => node.Status == RouterNodeStatus.Healthy);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Store_MultipleExpiredLeasesOnOneNode_DecrementByExactCount()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
            return;

        database.ApplyMigration();
        database.Truncate();
        var service = new RouterControlPlaneService(
            new ControlPlaneRepository(new ControlPlaneDbConnectionFactory(database.ConnectionString)),
            new NodeModelCapabilityStore());
        var now = DateTimeOffset.UtcNow;
        var node = await service.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        await service.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, now);
        for (var index = 0; index < 5; index++)
            await service.SelectRouterNodeAsync(null, $"request-{index}", now);

        await service.ReleaseExpiredLeasesAsync(now.AddMinutes(6));

        (await service.GetRouterNodeAsync(node.Id))!.InFlightRequests.Should().Be(0);
    }

}
