using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class GrpcRouterControlPlaneServiceIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public GrpcRouterControlPlaneServiceIntegrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ControlGrpcServer_RegisterDisableListWorkflow_ReturnsExpectedResponses()
    {
        var client = CreateClient();

        var registered = await client.RegisterRouterNodeAsync(new RegisterRouterNodeRequest
        {
            Name = "node-a",
            EndpointUrl = "https://router-a.example",
            TokenSecretName = "secret-a",
            CapacityWeight = 1
        });
        var listed = await client.ListRouterNodesAsync(new ListRouterNodesRequest());
        var disabled = await client.DisableRouterNodeAsync(new DisableRouterNodeRequest { NodeId = registered.NodeId });
        var listedAfterDisable = await client.ListRouterNodesAsync(new ListRouterNodesRequest());

        Assert.True(registered.Registered);
        Assert.Equal(registered.NodeId, Assert.Single(listed.Nodes).NodeId);
        Assert.True(disabled.Disabled);
        Assert.Equal("Disabled", Assert.Single(listedAfterDisable.Nodes).Status);
    }

    private global::OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane.RouterControlPlaneClient CreateClient()
    {
        var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SqlMigrations:Enabled"] = "false"
                })));
        var httpClient = configuredFactory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        return new global::OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane.RouterControlPlaneClient(channel);
    }
}
