using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class GrpcRouterControlPlaneServiceTests
{
    private static readonly IRouterNodeProviderClient NoOpProviderClient = new NoOpRouterNodeProviderClient();

    [Fact]
    public async Task SelectRouterNode_HealthyNode_ReturnsLease()
    {
        var control = CreateControlPlaneWithHealthyNode();
        var service = new GrpcRouterControlPlaneService(control, NoOpProviderClient);

        var response = await service.SelectRouterNode(
            new SelectRouterNodeRequest { RequestId = "req-1", ModelId = "gpt-test" },
            context: null!);

        Assert.NotEqual(string.Empty, response.LeaseId);
        Assert.NotEqual(string.Empty, response.NodeId);
        Assert.Equal("https://router-a.example/", response.EndpointUrl);
        Assert.Equal("secret-a", response.TokenSecretName);
        Assert.Equal(string.Empty, response.ErrorCode);
    }

    [Fact]
    public async Task SelectRouterNode_NoHealthyNodes_ReturnsRouterUnavailable()
    {
        var service = new GrpcRouterControlPlaneService(new RouterControlPlaneService(new InMemoryControlPlaneRepository()), NoOpProviderClient);

        var response = await service.SelectRouterNode(
            new SelectRouterNodeRequest { RequestId = "req-1", ModelId = "gpt-test" },
            context: null!);

        Assert.Equal(ErrorCodes.RouterUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task ReleaseRouterNode_ExistingLease_ReturnsAcceptedAndReleasesLoad()
    {
        var control = CreateControlPlaneWithHealthyNode();
        var service = new GrpcRouterControlPlaneService(control, NoOpProviderClient);
        var lease = control.SelectRouterNode("req-1", DateTimeOffset.UtcNow);

        var response = await service.ReleaseRouterNode(
            new ReleaseRouterNodeRequest { LeaseId = lease.LeaseId, Status = "succeeded" },
            context: null!);

        Assert.True(response.Accepted);
        var summary = Assert.Single(control.ListRouterNodes());
        Assert.Equal(0, summary.InFlightRequests);
    }

    [Fact]
    public async Task RegisterAndListRouterNode_ValidRequest_ReturnsNodeSummary()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var service = new GrpcRouterControlPlaneService(control, NoOpProviderClient);

        var registered = await service.RegisterRouterNode(new RegisterRouterNodeRequest
        {
            Name = "node-a",
            EndpointUrl = "https://router-a.example",
            TokenSecretName = "secret-a",
            CapacityWeight = 2
        }, context: null!);
        var nodes = await service.ListRouterNodes(new ListRouterNodesRequest(), context: null!);

        Assert.True(registered.Registered);
        Assert.NotEmpty(registered.NodeId);
        var node = Assert.Single(nodes.Nodes);
        Assert.Equal(registered.NodeId, node.NodeId);
        Assert.Equal("node-a", node.Name);
        Assert.Equal("https://router-a.example/", node.EndpointUrl);
    }

    [Fact]
    public async Task DisableRouterNode_ExistingNode_ReturnsDisabledAndListShowsDisabled()
    {
        var control = CreateControlPlaneWithHealthyNode();
        var service = new GrpcRouterControlPlaneService(control, NoOpProviderClient);
        var nodeId = Assert.Single(control.ListRouterNodes()).Id;

        var disabled = await service.DisableRouterNode(new DisableRouterNodeRequest { NodeId = nodeId }, context: null!);
        var nodes = await service.ListRouterNodes(new ListRouterNodesRequest(), context: null!);

        Assert.True(disabled.Disabled);
        Assert.Equal("Disabled", Assert.Single(nodes.Nodes).Status);
    }

    [Fact]
    public async Task GetRouterNodeDetails_ExistingNode_ReturnsConnectionAndIntegrationApiData()
    {
        var control = CreateControlPlaneWithHealthyNode();
        var nodeId = Assert.Single(control.ListRouterNodes()).Id;
        var service = new GrpcRouterControlPlaneService(control, new StubProviderClient());

        var response = await service.GetRouterNodeDetails(
            new GetRouterNodeDetailsRequest { NodeId = nodeId, IncludeInactiveProviders = true },
            context: null!);

        Assert.Equal(string.Empty, response.ErrorCode);
        Assert.Equal(nodeId, response.Node.NodeId);
        Assert.Equal("secret-a", response.Node.TokenSecretName);
        Assert.Equal(1, response.Node.CapacityWeight);
        var provider = Assert.Single(response.Providers);
        Assert.Equal("conn-1", provider.ConnectionId);
        Assert.Contains("\"message\":\"ok\"", provider.UsageJson);
        var key = Assert.Single(response.Keys);
        Assert.Equal("key-1", key.Id);
        Assert.Equal("sk-or...t0kn", key.KeyPreview);
    }

    [Fact]
    public async Task RegisterRouterNode_InvalidEndpoint_ReturnsValidationFailed()
    {
        var service = new GrpcRouterControlPlaneService(new RouterControlPlaneService(new InMemoryControlPlaneRepository()), NoOpProviderClient);

        var response = await service.RegisterRouterNode(new RegisterRouterNodeRequest
        {
            Name = "node-a",
            EndpointUrl = "http://router-a.example",
            TokenSecretName = "secret-a",
            CapacityWeight = 1
        }, context: null!);

        Assert.False(response.Registered);
        Assert.Equal(ErrorCodes.ValidationFailed, response.ErrorCode);
    }

    private static RouterControlPlaneService CreateControlPlaneWithHealthyNode()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var node = control.RegisterNode(
            "node-a",
            new Uri("https://router-a.example"),
            "secret-a",
            1,
            DateTimeOffset.UtcNow);
        control.ApplyHealth(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        return control;
    }

    private sealed class NoOpRouterNodeProviderClient : IRouterNodeProviderClient
    {
        public Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
            RouterNode node,
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RouterProviderQuotaResponse());

        public Task<RouterIntegrationKeysResponse> ListIntegrationKeysAsync(
            RouterNode node,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RouterIntegrationKeysResponse());

        public Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
            RouterNode node,
            string connectionId,
            bool isActive,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterProviderModelsResponse> ListProviderModelsAsync(
            RouterNode node,
            string provider,
            string? source,
            bool includeCompatible,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
            RouterNode node,
            string name,
            string key,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
            RouterNode node,
            string keyId,
            bool isActive,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubProviderClient : IRouterNodeProviderClient
    {
        public Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
            RouterNode node,
            bool includeInactive,
            CancellationToken cancellationToken)
        {
            using var usage = System.Text.Json.JsonDocument.Parse("""{"message":"ok"}""");
            return Task.FromResult(new RouterProviderQuotaResponse
            {
                Providers =
                [
                    new RouterProviderQuotaEntry
                    {
                        ConnectionId = "conn-1",
                        Provider = "codex",
                        Name = "Work Codex",
                        AuthType = "oauth",
                        IsActive = true,
                        TestStatus = "active",
                        QuotaSupported = true,
                        Usage = usage.RootElement.Clone()
                    }
                ]
            });
        }

        public Task<RouterIntegrationKeysResponse> ListIntegrationKeysAsync(
            RouterNode node,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new RouterIntegrationKeysResponse
            {
                Keys =
                [
                    new RouterIntegrationKeyInfo
                    {
                        Id = "key-1",
                        Name = "OpenRussiaRouter issued key",
                        KeyPreview = "sk-or...t0kn",
                        MachineId = "machine-1",
                        IsActive = true,
                        ManagedBy = "openrussia-router",
                        CreatedAt = "2026-05-17T12:00:00.000Z"
                    }
                ]
            });
        }

        public Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
            RouterNode node,
            string connectionId,
            bool isActive,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterProviderModelsResponse> ListProviderModelsAsync(
            RouterNode node,
            string provider,
            string? source,
            bool includeCompatible,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
            RouterNode node,
            string name,
            string key,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
            RouterNode node,
            string keyId,
            bool isActive,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

