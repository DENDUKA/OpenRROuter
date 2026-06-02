using System.Net;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Infrastructure.Grpc;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class GrpcRouterControlPlaneServiceTests
{
    private static readonly IRouterNodeProvider NoOpProviderClient = new NoOpRouterNodeProvider();

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
        var service = CreateService(new RouterControlPlaneService(repo, new NodeModelCapabilityStore()), repo);

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
        var lease = await control.SelectRouterNodeAsync(null, "req-1", DateTimeOffset.UtcNow);

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
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
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
    public async Task UpdateRouterNodeControlToken_ExistingNode_ReturnsUpdatedAndDetailsShowNewToken()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var service = CreateService(control);
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;

        var updated = await service.UpdateRouterNodeControlToken(
            new UpdateRouterNodeControlTokenRequest { NodeId = nodeId, ControlToken = "secret-b" },
            context: null!);
        var details = await service.GetRouterNodeDetails(
            new GetRouterNodeDetailsRequest { NodeId = nodeId },
            context: null!);

        updated.Updated.Should().BeTrue();
        updated.ErrorCode.Should().Be(string.Empty);
        details.Node.ControlToken.Should().Be("secret-b");
    }

    [Fact]
    public async Task UpdateRouterNodeControlToken_MissingToken_ReturnsValidationFailed()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var service = CreateService(control);
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;

        var response = await service.UpdateRouterNodeControlToken(
            new UpdateRouterNodeControlTokenRequest { NodeId = nodeId, ControlToken = "" },
            context: null!);

        response.Updated.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task UpdateRouterNode_ExistingNode_ReturnsUpdatedAndListShowsNewValues()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var service = CreateService(control);
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;

        var updated = await service.UpdateRouterNode(
            new UpdateRouterNodeRequest
            {
                NodeId = nodeId,
                Name = "node-b",
                EndpointUrl = "http://f9router",
                ControlToken = "secret-b",
                CapacityWeight = 5
            },
            context: null!);
        var nodes = await service.ListRouterNodes(new ListRouterNodesRequest(), context: null!);
        var details = await service.GetRouterNodeDetails(
            new GetRouterNodeDetailsRequest { NodeId = nodeId },
            context: null!);

        updated.Updated.Should().BeTrue();
        updated.ErrorCode.Should().Be(string.Empty);
        var node = nodes.Nodes.Should().ContainSingle().Which;
        node.Name.Should().Be("node-b");
        node.EndpointUrl.Should().Be("http://f9router/");
        node.CapacityWeight.Should().Be(5);
        details.Node.ControlToken.Should().Be("secret-b");
    }

    [Fact]
    public async Task UpdateRouterNode_InvalidEndpoint_ReturnsValidationFailed()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var service = CreateService(control);
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;

        var response = await service.UpdateRouterNode(
            new UpdateRouterNodeRequest
            {
                NodeId = nodeId,
                Name = "node-b",
                EndpointUrl = "ftp://f9router",
                ControlToken = "secret-b",
                CapacityWeight = 5
            },
            context: null!);

        response.Updated.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
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
    public async Task RegisterRouterNode_InvalidScheme_ReturnsValidationFailed()
    {
        // ftp:// is not a valid scheme; http:// and https:// are both allowed
        var repo = new InMemoryControlPlaneRepository();
        var service = CreateService(new RouterControlPlaneService(repo, new NodeModelCapabilityStore()), repo);

        var response = await service.RegisterRouterNode(new RegisterRouterNodeRequest
        {
            Name = "node-a",
            EndpointUrl = "ftp://router-a.example",
            ControlToken = "secret-a",
            CapacityWeight = 1
        }, context: null!);

        response.Registered.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task RegisterRouterNode_HttpEndpoint_Succeeds()
    {
        // HTTP endpoints are allowed for internal k8s services
        var repo = new InMemoryControlPlaneRepository();
        var service = CreateService(new RouterControlPlaneService(repo, new NodeModelCapabilityStore()), repo);

        var response = await service.RegisterRouterNode(new RegisterRouterNodeRequest
        {
            Name = "node-k8s",
            EndpointUrl = "http://f9router",
            ControlToken = "secret-a",
            CapacityWeight = 1
        }, context: null!);

        response.Registered.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterRouterNode_MissingControlToken_ReturnsValidationFailed()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
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
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
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
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
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
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
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
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
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

    [Fact]
    public async Task GetRouterNodeDetails_ProviderHangs_ReturnsNodeWithApiError()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var service = CreateService(control, providerClient: new HangingProviderClient());
        var started = System.Diagnostics.Stopwatch.StartNew();

        var response = await service.GetRouterNodeDetails(
            new GetRouterNodeDetailsRequest { NodeId = nodeId },
            context: null!);

        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
        response.ErrorCode.Should().Be(string.Empty);
        response.Node.NodeId.Should().Be(nodeId);
        response.ApiError.Should().Contain("Provider quota unavailable");
        response.ApiError.Should().Contain("Integration keys unavailable");
    }

    private static GrpcRouterControlPlaneService CreateService(
        RouterControlPlaneService control,
        IControlPlaneRepository? repo = null,
        IRouterNodeProvider? providerClient = null)
    {
        return new GrpcRouterControlPlaneService(
            control,
            repo ?? new InMemoryControlPlaneRepository(),
            providerClient ?? NoOpProviderClient,
            NullLogger<GrpcRouterControlPlaneService>.Instance);
    }

    private static async Task<RouterControlPlaneService> CreateControlPlaneWithHealthyNodeAsync()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
        var node = await control.RegisterNodeAsync(
            "node-a",
            new Uri("https://router-a.example"),
            "secret-a",
            1,
            DateTimeOffset.UtcNow);
        await control.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        return control;
    }

    private sealed class NoOpRouterNodeProvider : IRouterNodeProvider
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

    private sealed class ThrowingProviderClient : IRouterNodeProvider
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

        public Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
            RouterNode node, string name, string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
            RouterNode node, string keyId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class HangingProviderClient : IRouterNodeProvider
    {
        public async Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
            RouterNode node,
            bool includeInactive,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new RouterProviderQuotaResponse();
        }

        public async Task<RouterIntegrationKeysResponse> ListIntegrationKeysAsync(
            RouterNode node,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new RouterIntegrationKeysResponse();
        }

        public Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
            RouterNode node, string connectionId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
            RouterNode node, string name, string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
            RouterNode node, string keyId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubProviderClient : IRouterNodeProvider
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
