using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var service = CreateService(control);

        var response = await service.SelectRouterNode(
            new SelectRouterNodeRequest { RequestId = "req-1", ModelId = "gpt-test" },
            context: null!);

        response.LeaseId.Should().NotBe(string.Empty);
        response.NodeId.Should().NotBe(string.Empty);
        response.EndpointUrl.Should().Be("https://router-a.example/");
        response.ControlToken.Should().Be("secret-a");
        response.ErrorCode.Should().Be(string.Empty);
    }

    [Fact]
    public async Task SelectRouterNode_NoHealthyNodes_ReturnsRouterUnavailable()
    {
        var repo = new InMemoryControlPlaneRepository();
        var service = CreateService(new RouterControlPlaneService(repo), repo);

        var response = await service.SelectRouterNode(
            new SelectRouterNodeRequest { RequestId = "req-1", ModelId = "gpt-test" },
            context: null!);

        response.ErrorCode.Should().Be(ErrorCodes.RouterUnavailable);
    }

    [Fact]
    public async Task ReleaseRouterNode_ExistingLease_ReturnsAcceptedAndReleasesLoad()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var service = CreateService(control);
        var lease = await control.SelectRouterNodeAsync("req-1", DateTimeOffset.UtcNow);

        var response = await service.ReleaseRouterNode(
            new ReleaseRouterNodeRequest { LeaseId = lease.LeaseId, Status = "succeeded" },
            context: null!);

        response.Accepted.Should().BeTrue();
        var summary = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(0);
    }

    [Fact]
    public async Task RegisterAndListRouterNode_ValidRequest_ReturnsNodeSummary()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var service = CreateService(control);

        var registered = await service.RegisterRouterNode(new RegisterRouterNodeRequest
        {
            Name = "node-a",
            EndpointUrl = "https://router-a.example",
            ControlToken = "secret-a",
            CapacityWeight = 2
        }, context: null!);
        var nodes = await service.ListRouterNodes(new ListRouterNodesRequest(), context: null!);

        registered.Registered.Should().BeTrue();
        registered.NodeId.Should().NotBeNullOrEmpty();
        var node = nodes.Nodes.Should().ContainSingle().Which;
        node.NodeId.Should().Be(registered.NodeId);
        node.Name.Should().Be("node-a");
        node.EndpointUrl.Should().Be("https://router-a.example/");
    }

    [Fact]
    public async Task DisableRouterNode_ExistingNode_ReturnsDisabledAndListShowsDisabled()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var service = CreateService(control);
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;

        var disabled = await service.DisableRouterNode(new DisableRouterNodeRequest { NodeId = nodeId }, context: null!);
        var nodes = await service.ListRouterNodes(new ListRouterNodesRequest(), context: null!);

        disabled.Disabled.Should().BeTrue();
        nodes.Nodes.Should().ContainSingle().Which.Status.Should().Be("Disabled");
    }

    [Fact]
    public async Task GetRouterNodeDetails_ExistingNode_ReturnsConnectionAndIntegrationApiData()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var service = CreateService(control, providerClient: new StubProviderClient());

        var response = await service.GetRouterNodeDetails(
            new GetRouterNodeDetailsRequest { NodeId = nodeId, IncludeInactiveProviders = true },
            context: null!);

        response.ErrorCode.Should().Be(string.Empty);
        response.Node.NodeId.Should().Be(nodeId);
        response.Node.ControlToken.Should().Be("secret-a");
        response.Node.CapacityWeight.Should().Be(1);
        var provider = response.Providers.Should().ContainSingle().Which;
        provider.ConnectionId.Should().Be("conn-1");
        provider.UsageJson.Should().Contain("\"message\":\"ok\"");
        var key = response.Keys.Should().ContainSingle().Which;
        key.Id.Should().Be("key-1");
        key.KeyPreview.Should().Be("sk-or...t0kn");
    }

    [Fact]
    public async Task RegisterRouterNode_InvalidEndpoint_ReturnsValidationFailed()
    {
        var repo = new InMemoryControlPlaneRepository();
        var service = CreateService(new RouterControlPlaneService(repo), repo);

        var response = await service.RegisterRouterNode(new RegisterRouterNodeRequest
        {
            Name = "node-a",
            EndpointUrl = "http://router-a.example",
            ControlToken = "secret-a",
            CapacityWeight = 1
        }, context: null!);

        response.Registered.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task RegisterRouterNode_MissingControlToken_ReturnsValidationFailed()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var service = CreateService(control);

        var response = await service.RegisterRouterNode(new RegisterRouterNodeRequest
        {
            Name = "node-a",
            EndpointUrl = "https://router-a.example",
            ControlToken = "",
            CapacityWeight = 1
        }, context: null!);

        response.Registered.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
        (await control.ListNodesForHealthChecksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DisableRouterNode_UnknownNode_ReturnsValidationFailed()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var service = CreateService(control);

        var response = await service.DisableRouterNode(
            new DisableRouterNodeRequest { NodeId = "does-not-exist" },
            context: null!);

        response.Disabled.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task DeleteRouterNode_ExistingNode_ReturnsDeletedAndNodeRemovedFromList()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var service = CreateService(control);

        var deleted = await service.DeleteRouterNode(
            new DeleteRouterNodeRequest { NodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id },
            context: null!);
        var nodes = await service.ListRouterNodes(new ListRouterNodesRequest(), context: null!);

        deleted.Deleted.Should().BeTrue();
        nodes.Nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteRouterNode_UnknownNode_ReturnsValidationFailed()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var service = CreateService(control);

        var response = await service.DeleteRouterNode(
            new DeleteRouterNodeRequest { NodeId = "does-not-exist" },
            context: null!);

        response.Deleted.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task EnableRouterNode_DisabledNode_ReturnsEnabled()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var service = CreateService(control);
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        await service.DisableRouterNode(new DisableRouterNodeRequest { NodeId = nodeId }, context: null!);

        var response = await service.EnableRouterNode(
            new EnableRouterNodeRequest { NodeId = nodeId },
            context: null!);

        response.Enabled.Should().BeTrue();
        response.ErrorCode.Should().Be(string.Empty);
    }

    [Fact]
    public async Task EnableRouterNode_UnknownNode_ReturnsValidationFailed()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var service = CreateService(control);

        var response = await service.EnableRouterNode(
            new EnableRouterNodeRequest { NodeId = "does-not-exist" },
            context: null!);

        response.Enabled.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task GetRouterNodeDetails_UnknownNode_ReturnsValidationFailed()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var service = CreateService(control);

        var response = await service.GetRouterNodeDetails(
            new GetRouterNodeDetailsRequest { NodeId = "does-not-exist" },
            context: null!);

        response.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task GetRouterNodeDetails_ProviderThrows_ReturnsNodeWithApiError()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var service = CreateService(control, providerClient: new ThrowingProviderClient());

        var response = await service.GetRouterNodeDetails(
            new GetRouterNodeDetailsRequest { NodeId = nodeId },
            context: null!);

        response.ErrorCode.Should().Be(string.Empty);
        response.Node.NodeId.Should().Be(nodeId);
        response.ApiError.Should().NotBeNullOrEmpty();
    }

    private static GrpcRouterControlPlaneService CreateService(
        RouterControlPlaneService control,
        IControlPlaneRepository? repo = null,
        IRouterNodeProviderClient? providerClient = null)
    {
        return new GrpcRouterControlPlaneService(
            control,
            repo ?? new InMemoryControlPlaneRepository(),
            providerClient ?? NoOpProviderClient,
            NullLogger<GrpcRouterControlPlaneService>.Instance);
    }

    private static async Task<RouterControlPlaneService> CreateControlPlaneWithHealthyNodeAsync()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var node = await control.RegisterNodeAsync(
            "node-a",
            new Uri("https://router-a.example"),
            "secret-a",
            1,
            DateTimeOffset.UtcNow);
        await control.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
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

        public Task<NodeModelsResponse> ListModelsAsync(
            RouterNode node,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NodeModelsResponse());

        public Task<RouterProviderListResponse> ListProvidersAsync(
            RouterNode node,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RouterProviderListResponse());
    }

    private sealed class ThrowingProviderClient : IRouterNodeProviderClient
    {
        public Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
            RouterNode node, bool includeInactive, CancellationToken cancellationToken) =>
            throw new RouterNodeProviderException(System.Net.HttpStatusCode.ServiceUnavailable, "unavailable", "Provider unavailable");

        public Task<RouterIntegrationKeysResponse> ListIntegrationKeysAsync(
            RouterNode node, CancellationToken cancellationToken) =>
            throw new RouterNodeProviderException(System.Net.HttpStatusCode.ServiceUnavailable, "unavailable", "Provider unavailable");

        public Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
            RouterNode node, string connectionId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterProviderModelsResponse> ListProviderModelsAsync(
            RouterNode node, string provider, string? source, bool includeCompatible, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
            RouterNode node, string name, string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
            RouterNode node, string keyId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NodeModelsResponse> ListModelsAsync(
            RouterNode node, CancellationToken cancellationToken) =>
            Task.FromResult(new NodeModelsResponse());

        public Task<RouterProviderListResponse> ListProvidersAsync(
            RouterNode node, CancellationToken cancellationToken) =>
            throw new RouterNodeProviderException(System.Net.HttpStatusCode.ServiceUnavailable, "unavailable", "Provider unavailable");
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

        public Task<NodeModelsResponse> ListModelsAsync(
            RouterNode node,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NodeModelsResponse
            {
                Data = [new NodeModelInfo { Id = "minimax/MiniMax-M2.7" }]
            });

        public Task<RouterProviderListResponse> ListProvidersAsync(
            RouterNode node,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
