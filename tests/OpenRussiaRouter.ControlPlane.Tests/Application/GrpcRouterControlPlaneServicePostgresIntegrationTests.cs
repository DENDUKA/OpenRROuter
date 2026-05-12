using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using OpenRussiaRouter.ControlPlane.Tests.Infrastructure.Persistence;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class GrpcRouterControlPlaneServicePostgresIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ControlGrpcServer_PostgresWorkflow_PersistsRouterNodeLifecycle()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        database.ApplyMigration();
        database.Truncate();
        await using var factory = CreateFactory(database.ConnectionString);
        var client = CreateClient(factory);

        var registered = await client.RegisterRouterNodeAsync(new RegisterRouterNodeRequest
        {
            Name = "node-pg",
            EndpointUrl = "https://router-pg.example",
            TokenSecretName = "secret-pg",
            CapacityWeight = 1
        });
        var listed = await client.ListRouterNodesAsync(new ListRouterNodesRequest());
        _ = await client.DisableRouterNodeAsync(new DisableRouterNodeRequest { NodeId = registered.NodeId });
        var listedAfterDisable = await client.ListRouterNodesAsync(new ListRouterNodesRequest());

        Assert.True(registered.Registered);
        Assert.Equal("node-pg", Assert.Single(listed.Nodes).Name);
        Assert.Equal("Disabled", Assert.Single(listedAfterDisable.Nodes).Status);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ControlPlaneDb"] = connectionString
                })));
    }

    private static global::OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane.RouterControlPlaneClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        return new global::OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane.RouterControlPlaneClient(channel);
    }
}
