using Dapper;
using Npgsql;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

namespace OpenRussiaRouter.ControlPlane.Tests.Infrastructure.Persistence;

public sealed class DapperControlPlaneStorePostgresTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public void Store_AppliedMigration_PersistsNodeLeaseAndCleanup()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        ApplyMigration(connectionString, "control");
        using var connection = new NpgsqlConnection(connectionString);
        connection.Execute("truncate control.router_leases, control.router_nodes restart identity cascade;");
        var service = new RouterControlPlaneService(
            new DapperControlPlaneStore(new NpgsqlControlPlaneDbConnectionFactory(connectionString)));
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

    private static string? GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("ORR_CONTROL_POSTGRES_TEST_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ORR_POSTGRES_TEST_CONNECTION");
    }

    private static void ApplyMigration(string connectionString, string service)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Execute(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "db", "migrations", service, "001_initial.sql")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenRussiaRouter.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
