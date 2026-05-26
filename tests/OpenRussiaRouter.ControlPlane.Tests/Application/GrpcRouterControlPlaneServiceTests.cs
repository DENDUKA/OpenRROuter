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
        var service = CreateService(new RouterControlPlaneService(repo), repo);

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
        var service = CreateService(new RouterControlPlaneService(repo), repo);

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

    [Fact]
    public async Task ListNodeModelCatalog_NoEnabledNodes_ReturnsEmpty()
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var service = CreateService(control);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        response.Entries.Should().BeEmpty();
        response.UnreachableNodeIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ListNodeModelCatalog_ProviderCatalog_MapsRichFields()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var stub = new CatalogProviderStub();
        stub.ProvidersWithModelsByNode[nodeId] = _ => Task.FromResult(new RouterProvidersWithModelsResponse
        {
            Connections =
            [
                new RouterProviderWithModels
                {
                    Id = "c-1",
                    Provider = "minimax",
                    IsActive = true,
                    Models =
                    [
                        new RouterProviderModelInfo
                        {
                            Id = "minimax-m2-id",
                            PublicId = "minimax/MiniMax-M2.7",
                            Name = "MiniMax M2.7",
                            Type = "chat",
                            Enabled = true,
                            Sources = ["official", "openrouter"]
                        }
                    ]
                }
            ]
        });
        var service = CreateService(control, providerClient: stub);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        response.UnreachableNodeIds.Should().BeEmpty();
        var entry = response.Entries.Should().ContainSingle().Which;
        entry.UpstreamId.Should().Be("minimax-m2-id");
        entry.PublicId.Should().Be("minimax/MiniMax-M2.7");
        entry.DisplayName.Should().Be("MiniMax M2.7");
        entry.ModelType.Should().Be("chat");
        entry.SourceProviders.Should().BeEquivalentTo(new[] { "official", "openrouter" });
        entry.NodeIds.Should().ContainSingle().Which.Should().Be(nodeId);
    }

    [Fact]
    public async Task ListNodeModelCatalog_WithModels404_FallsBackToV1Models_NotUnreachable()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var stub = new CatalogProviderStub();
        stub.ProvidersWithModelsByNode[nodeId] = _ => Task.FromException<RouterProvidersWithModelsResponse>(
            new RouterNodeProviderException(HttpStatusCode.NotFound, "not_found", "no /api/providers"));
        stub.ModelsByNode[nodeId] = _ => Task.FromResult(new RouterModelsBuilder()
            .With("minimax/MiniMax-M2.7")
            .With("gpt-test")
            .Build());
        var service = CreateService(control, providerClient: stub);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        response.UnreachableNodeIds.Should().BeEmpty();
        response.Entries.Should().HaveCount(2);
        response.Entries.Select(e => e.UpstreamId).Should().Equal("gpt-test", "minimax/MiniMax-M2.7");
        foreach (var entry in response.Entries)
        {
            entry.PublicId.Should().Be(entry.UpstreamId);
            entry.DisplayName.Should().Be(string.Empty);
            entry.ModelType.Should().Be(string.Empty);
            entry.SourceProviders.Should().BeEmpty();
            entry.NodeIds.Should().ContainSingle().Which.Should().Be(nodeId);
        }
    }

    [Fact]
    public async Task ListNodeModelCatalog_WithModelsNon404_MarksNodeUnreachable()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var stub = new CatalogProviderStub();
        stub.ProvidersWithModelsByNode[nodeId] = _ => Task.FromException<RouterProvidersWithModelsResponse>(
            new RouterNodeProviderException(HttpStatusCode.ServiceUnavailable, "unavailable", "503"));
        var service = CreateService(control, providerClient: stub);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        response.Entries.Should().BeEmpty();
        response.UnreachableNodeIds.Should().ContainSingle().Which.Should().Be(nodeId);
        stub.ListModelsCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ListNodeModelCatalog_InactiveConnections_AreSkipped()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var stub = new CatalogProviderStub();
        stub.ProvidersWithModelsByNode[nodeId] = _ => Task.FromResult(new RouterProvidersWithModelsResponse
        {
            Connections =
            [
                new RouterProviderWithModels
                {
                    Id = "c-1",
                    Provider = "minimax",
                    IsActive = false,
                    Models = [new RouterProviderModelInfo { Id = "inactive-model", Enabled = true }]
                },
                new RouterProviderWithModels
                {
                    Id = "c-2",
                    Provider = "openai",
                    IsActive = true,
                    Models = [new RouterProviderModelInfo { Id = "gpt-test", PublicId = "gpt-test", Enabled = true }]
                }
            ]
        });
        var service = CreateService(control, providerClient: stub);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        response.Entries.Should().ContainSingle().Which.UpstreamId.Should().Be("gpt-test");
    }

    [Fact]
    public async Task ListNodeModelCatalog_TwoActiveConnections_BothContributeModels()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var stub = new CatalogProviderStub();
        stub.ProvidersWithModelsByNode[nodeId] = _ => Task.FromResult(new RouterProvidersWithModelsResponse
        {
            Connections =
            [
                new RouterProviderWithModels
                {
                    Id = "c-1",
                    Provider = "openrouter",
                    IsActive = true,
                    Models = [new RouterProviderModelInfo { Id = "m-1", PublicId = "m-1", Enabled = true }]
                },
                new RouterProviderWithModels
                {
                    Id = "c-2",
                    Provider = "openrouter",
                    IsActive = true,
                    Models = [new RouterProviderModelInfo { Id = "m-2", PublicId = "m-2", Enabled = true }]
                }
            ]
        });
        var service = CreateService(control, providerClient: stub);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        stub.ListProvidersWithModelsCalls.Should().ContainSingle().Which.Should().Be(nodeId);
        response.Entries.Select(e => e.UpstreamId).Should().BeEquivalentTo(["m-1", "m-2"]);
    }

    [Fact]
    public async Task ListNodeModelCatalog_ConnectionWithEmptyModels_ContributesNothingToNode()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var stub = new CatalogProviderStub();
        stub.ProvidersWithModelsByNode[nodeId] = _ => Task.FromResult(new RouterProvidersWithModelsResponse
        {
            Connections =
            [
                new RouterProviderWithModels
                {
                    Id = "c-1",
                    Provider = "minimax",
                    IsActive = true,
                    Models = []
                },
                new RouterProviderWithModels
                {
                    Id = "c-2",
                    Provider = "openai",
                    IsActive = true,
                    Models = [new RouterProviderModelInfo { Id = "gpt-test", PublicId = "gpt-test", Enabled = true }]
                }
            ]
        });
        var service = CreateService(control, providerClient: stub);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        response.UnreachableNodeIds.Should().BeEmpty();
        response.Entries.Should().ContainSingle().Which.UpstreamId.Should().Be("gpt-test");
    }

    [Fact]
    public async Task ListNodeModelCatalog_DisabledProviderModels_AreExcluded()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var stub = new CatalogProviderStub();
        stub.ProvidersWithModelsByNode[nodeId] = _ => Task.FromResult(new RouterProvidersWithModelsResponse
        {
            Connections =
            [
                new RouterProviderWithModels
                {
                    Id = "c-1",
                    Provider = "minimax",
                    IsActive = true,
                    Models =
                    [
                        new RouterProviderModelInfo { Id = "enabled-id", PublicId = "enabled-id", Enabled = true },
                        new RouterProviderModelInfo { Id = "disabled-id", PublicId = "disabled-id", Enabled = false }
                    ]
                }
            ]
        });
        var service = CreateService(control, providerClient: stub);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        response.Entries.Should().ContainSingle().Which.UpstreamId.Should().Be("enabled-id");
    }

    [Fact]
    public async Task ListNodeModelCatalog_RichRank2EmptyPublicId_FallsBackToUpstreamId()
    {
        var control = await CreateControlPlaneWithHealthyNodeAsync();
        var nodeId = (await control.ListRouterNodesAsync()).Should().ContainSingle().Which.Id;
        var stub = new CatalogProviderStub();
        stub.ProvidersWithModelsByNode[nodeId] = _ => Task.FromResult(new RouterProvidersWithModelsResponse
        {
            Connections =
            [
                new RouterProviderWithModels
                {
                    Id = "c-1",
                    Provider = "minimax",
                    IsActive = true,
                    Models =
                    [
                        new RouterProviderModelInfo
                        {
                            Id = "minimax-m2-id",
                            PublicId = string.Empty,
                            Name = "MiniMax",
                            Type = "chat",
                            Enabled = true
                        }
                    ]
                }
            ]
        });
        var service = CreateService(control, providerClient: stub);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        var entry = response.Entries.Should().ContainSingle().Which;
        entry.UpstreamId.Should().Be("minimax-m2-id");
        entry.PublicId.Should().Be("minimax-m2-id");
    }

    [Fact]
    public async Task ListNodeModelCatalog_RichRank2_OverwritesFallbackRank1WhenFallbackArrivesFirst()
    {
        var (control, fallbackId, richId) = await CreateTwoEnabledNodesAsync("node-fallback", "node-rich");
        var stub = new CatalogProviderStub();

        stub.ProvidersWithModelsByNode[fallbackId] = _ => Task.FromException<RouterProvidersWithModelsResponse>(
            new RouterNodeProviderException(HttpStatusCode.NotFound, "not_found", "old node"));
        stub.ModelsByNode[fallbackId] = _ => Task.FromResult(new RouterModelsBuilder()
            .With("minimax-m2-id")
            .Build());

        stub.ProvidersWithModelsByNode[richId] = _ => Task.FromResult(new RouterProvidersWithModelsResponse
        {
            Connections =
            [
                new RouterProviderWithModels
                {
                    Id = "c-1",
                    Provider = "minimax",
                    IsActive = true,
                    Models =
                    [
                        new RouterProviderModelInfo
                        {
                            Id = "minimax-m2-id",
                            PublicId = "minimax/MiniMax-M2.7",
                            Name = "MiniMax",
                            Type = "chat",
                            Enabled = true,
                            Sources = ["official"]
                        }
                    ]
                }
            ]
        });

        var service = CreateService(control, providerClient: stub);
        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        var entry = response.Entries.Should().ContainSingle().Which;
        entry.PublicId.Should().Be("minimax/MiniMax-M2.7");
        entry.DisplayName.Should().Be("MiniMax");
        entry.ModelType.Should().Be("chat");
        entry.SourceProviders.Should().BeEquivalentTo(new[] { "official" });
        entry.NodeIds.Should().BeEquivalentTo(new[] { fallbackId, richId });
    }

    [Fact]
    public async Task ListNodeModelCatalog_LaterRank2WithNonEmptyPublicId_FillsEarlierRank2EmptyPublicId()
    {
        var (control, firstId, secondId) = await CreateTwoEnabledNodesAsync("node-empty-publicid", "node-with-publicid");
        var stub = new CatalogProviderStub();

        stub.ProvidersWithModelsByNode[firstId] = _ => Task.FromResult(new RouterProvidersWithModelsResponse
        {
            Connections =
            [
                new RouterProviderWithModels
                {
                    Id = "c-1",
                    Provider = "minimax",
                    IsActive = true,
                    Models =
                    [
                        new RouterProviderModelInfo
                        {
                            Id = "minimax-m2-id",
                            PublicId = string.Empty,
                            Name = "MiniMax First",
                            Type = "chat",
                            Enabled = true
                        }
                    ]
                }
            ]
        });

        stub.ProvidersWithModelsByNode[secondId] = _ => Task.FromResult(new RouterProvidersWithModelsResponse
        {
            Connections =
            [
                new RouterProviderWithModels
                {
                    Id = "c-2",
                    Provider = "minimax",
                    IsActive = true,
                    Models =
                    [
                        new RouterProviderModelInfo
                        {
                            Id = "minimax-m2-id",
                            PublicId = "minimax/MiniMax-M2.7",
                            Name = "MiniMax Second",
                            Type = "chat",
                            Enabled = true
                        }
                    ]
                }
            ]
        });

        var service = CreateService(control, providerClient: stub);
        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        var entry = response.Entries.Should().ContainSingle().Which;
        entry.PublicId.Should().Be("minimax/MiniMax-M2.7");
        entry.DisplayName.Should().Be("MiniMax First");
        entry.NodeIds.Should().BeEquivalentTo(new[] { firstId, secondId });
    }

    [Fact]
    public async Task ListNodeModelCatalog_SourceProvidersAndNodeIds_AreDeduplicated()
    {
        var (control, firstId, secondId) = await CreateTwoEnabledNodesAsync("node-a", "node-b");
        var stub = new CatalogProviderStub();

        Task<RouterProvidersWithModelsResponse> MakeResponse(CancellationToken _) => Task.FromResult(
            new RouterProvidersWithModelsResponse
            {
                Connections =
                [
                    new RouterProviderWithModels
                    {
                        Id = "c-1",
                        Provider = "minimax",
                        IsActive = true,
                        Models =
                        [
                            new RouterProviderModelInfo
                            {
                                Id = "minimax-m2-id",
                                PublicId = "minimax/MiniMax-M2.7",
                                Enabled = true,
                                Sources = ["official", "openrouter"]
                            }
                        ]
                    }
                ]
            });

        stub.ProvidersWithModelsByNode[firstId] = MakeResponse;
        stub.ProvidersWithModelsByNode[secondId] = MakeResponse;

        var service = CreateService(control, providerClient: stub);
        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        var entry = response.Entries.Should().ContainSingle().Which;
        entry.SourceProviders.Should().BeEquivalentTo(new[] { "official", "openrouter" });
        entry.NodeIds.Should().BeEquivalentTo(new[] { firstId, secondId });
    }

    [Fact]
    public async Task ListNodeModelCatalog_CollectsNodesInParallel_AndPropagatesCancellationToken()
    {
        var (control, firstId, secondId) = await CreateTwoEnabledNodesAsync("node-a", "node-b");
        var stub = new CatalogProviderStub();
        using var cts = new CancellationTokenSource();
        var providersEntered = new TaskCompletionSource[2];
        providersEntered[0] = new TaskCompletionSource();
        providersEntered[1] = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        stub.ProvidersWithModelsByNode[firstId] = async ct =>
        {
            providersEntered[0].SetResult();
            await release.Task;
            return new RouterProvidersWithModelsResponse { Connections = [] };
        };
        stub.ProvidersWithModelsByNode[secondId] = async ct =>
        {
            providersEntered[1].SetResult();
            await release.Task;
            return new RouterProvidersWithModelsResponse { Connections = [] };
        };

        var service = CreateService(control, providerClient: stub);
        var responseTask = service.ListNodeModelCatalog(
            new ListNodeModelCatalogRequest(),
            new TestServerCallContext(cts.Token));

        await Task.WhenAll(providersEntered[0].Task, providersEntered[1].Task)
            .WaitAsync(TimeSpan.FromSeconds(5));

        release.SetResult();
        await responseTask;

        stub.ReceivedTokens.Should().NotBeEmpty();
        stub.ReceivedTokens.Should().OnlyContain(t => t.CanBeCanceled);
    }

    private async Task<(RouterControlPlaneService Control, string FirstId, string SecondId)> CreateTwoEnabledNodesAsync(
        string firstName, string secondName)
    {
        var control = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var first = await control.RegisterNodeAsync(
            firstName, new Uri($"https://{firstName}.example"), "secret", 1, DateTimeOffset.UtcNow);
        var second = await control.RegisterNodeAsync(
            secondName, new Uri($"https://{secondName}.example"), "secret", 1, DateTimeOffset.UtcNow);
        await control.ApplyHealthAsync(first.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        await control.ApplyHealthAsync(second.Id, RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        return (control, first.Id, second.Id);
    }

    private sealed class RouterModelsBuilder
    {
        private readonly List<NodeModelInfo> _models = [];
        public RouterModelsBuilder With(string id)
        {
            _models.Add(new NodeModelInfo { Id = id });
            return this;
        }
        public NodeModelsResponse Build() => new() { Data = _models };
    }

    private sealed class CatalogProviderStub : IRouterNodeProvider
    {
        public Dictionary<string, Func<CancellationToken, Task<RouterProvidersWithModelsResponse>>> ProvidersWithModelsByNode { get; } = new();
        public Dictionary<string, Func<CancellationToken, Task<NodeModelsResponse>>> ModelsByNode { get; } = new();
        public List<string> ListProvidersWithModelsCalls { get; } = new();
        public List<string> ListModelsCalls { get; } = new();
        public List<CancellationToken> ReceivedTokens { get; } = new();

        public string DescribeListProvidersRequest(RouterNode node) => $"GET provider-list:{node.Id}";

        public string DescribeListModelsRequest(RouterNode node) => $"GET model-list:{node.Id}";

        public Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
            RouterNode node, bool includeInactive, CancellationToken ct) =>
            Task.FromResult(new RouterProviderQuotaResponse());

        public Task<RouterProvidersWithModelsResponse> ListProvidersWithModelsAsync(
            RouterNode node, CancellationToken ct)
        {
            lock (ListProvidersWithModelsCalls) ListProvidersWithModelsCalls.Add(node.Id);
            lock (ReceivedTokens) ReceivedTokens.Add(ct);
            return ProvidersWithModelsByNode.TryGetValue(node.Id, out var fn)
                ? fn(ct)
                : Task.FromResult(new RouterProvidersWithModelsResponse());
        }

        public Task<NodeModelsResponse> ListModelsAsync(RouterNode node, CancellationToken ct)
        {
            lock (ListModelsCalls) ListModelsCalls.Add(node.Id);
            lock (ReceivedTokens) ReceivedTokens.Add(ct);
            return ModelsByNode.TryGetValue(node.Id, out var fn)
                ? fn(ct)
                : Task.FromResult(new NodeModelsResponse());
        }

        public Task<RouterIntegrationKeysResponse> ListIntegrationKeysAsync(
            RouterNode node, CancellationToken ct) =>
            Task.FromResult(new RouterIntegrationKeysResponse());

        public Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
            RouterNode node, string connectionId, bool isActive, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
            RouterNode node, string name, string key, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
            RouterNode node, string keyId, bool isActive, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        public TestServerCallContext(CancellationToken cancellationToken)
        {
            CancellationTokenCore = cancellationToken;
        }

        protected override string MethodCore => "/test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "localhost";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore { get; }
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; } = Status.DefaultSuccess;
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(string.Empty, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
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

    private sealed class NoOpRouterNodeProvider : IRouterNodeProvider
    {
        public string DescribeListProvidersRequest(RouterNode node) => $"GET provider-list:{node.Id}";

        public string DescribeListModelsRequest(RouterNode node) => $"GET model-list:{node.Id}";

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

        public Task<NodeModelsResponse> ListModelsAsync(
            RouterNode node,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NodeModelsResponse());

        public Task<RouterProvidersWithModelsResponse> ListProvidersWithModelsAsync(
            RouterNode node,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RouterProvidersWithModelsResponse());
    }

    private sealed class ThrowingProviderClient : IRouterNodeProvider
    {
        public string DescribeListProvidersRequest(RouterNode node) => $"GET provider-list:{node.Id}";

        public string DescribeListModelsRequest(RouterNode node) => $"GET model-list:{node.Id}";

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

        public Task<NodeModelsResponse> ListModelsAsync(
            RouterNode node, CancellationToken cancellationToken) =>
            Task.FromResult(new NodeModelsResponse());

        public Task<RouterProvidersWithModelsResponse> ListProvidersWithModelsAsync(
            RouterNode node, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class HangingProviderClient : IRouterNodeProvider
    {
        public string DescribeListProvidersRequest(RouterNode node) => $"GET provider-list:{node.Id}";

        public string DescribeListModelsRequest(RouterNode node) => $"GET model-list:{node.Id}";

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

        public Task<NodeModelsResponse> ListModelsAsync(
            RouterNode node, CancellationToken cancellationToken) =>
            Task.FromResult(new NodeModelsResponse());

        public Task<RouterProvidersWithModelsResponse> ListProvidersWithModelsAsync(
            RouterNode node, CancellationToken cancellationToken) =>
            Task.FromResult(new RouterProvidersWithModelsResponse());
    }

    private sealed class StubProviderClient : IRouterNodeProvider
    {
        public string DescribeListProvidersRequest(RouterNode node) => $"GET provider-list:{node.Id}";

        public string DescribeListModelsRequest(RouterNode node) => $"GET model-list:{node.Id}";

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

        public Task<NodeModelsResponse> ListModelsAsync(
            RouterNode node,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NodeModelsResponse
            {
                Data = [new NodeModelInfo { Id = "minimax/MiniMax-M2.7" }]
            });

        public Task<RouterProvidersWithModelsResponse> ListProvidersWithModelsAsync(
            RouterNode node,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
