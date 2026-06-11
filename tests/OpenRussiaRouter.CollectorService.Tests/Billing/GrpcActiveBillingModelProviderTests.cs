using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.CollectorService.Application.Billing;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Tests.Collection;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.CollectorService.Tests.Billing;

public class GrpcActiveBillingModelProviderTests
{
    private static AsyncUnaryCall<T> MakeAsyncUnaryCall<T>(T response)
        => new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    [Fact]
    public async Task ListActiveModelsAsync_UsesCache_WithinRefreshInterval()
    {
        var client = new Mock<BillingGrpc.BillingServiceClient>();
        client.Setup(c => c.ListModelsAsync(It.IsAny<ListModelsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(MakeAsyncUnaryCall(Response(("ORR/GPT", "router/gpt", true))));

        var provider = Build(client.Object);

        var first = await provider.ListActiveModelsAsync(CancellationToken.None);
        var second = await provider.ListActiveModelsAsync(CancellationToken.None);

        first.Should().Equal(second);
        second.Should().ContainSingle().Which.Should().Be(new ActiveBillingModel("ORR/GPT", "router/gpt"));
        client.Verify(c => c.ListModelsAsync(It.IsAny<ListModelsRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListActiveModelsAsync_RefreshesCache_AfterRefreshInterval()
    {
        var client = new Mock<BillingGrpc.BillingServiceClient>();
        client.SetupSequence(c => c.ListModelsAsync(It.IsAny<ListModelsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(MakeAsyncUnaryCall(Response(("ORR/A", "router/a", true))))
            .Returns(MakeAsyncUnaryCall(Response(("ORR/B", "router/b", true))));
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var provider = Build(client.Object, time);

        var first = await provider.ListActiveModelsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        var second = await provider.ListActiveModelsAsync(CancellationToken.None);

        first.Should().ContainSingle(m => m.ModelId == "ORR/A");
        second.Should().ContainSingle(m => m.ModelId == "ORR/B");
        client.Verify(c => c.ListModelsAsync(It.IsAny<ListModelsRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ListActiveModelsAsync_ReturnsPreviousCache_WhenRefreshFails()
    {
        var client = new Mock<BillingGrpc.BillingServiceClient>();
        client.SetupSequence(c => c.ListModelsAsync(It.IsAny<ListModelsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(MakeAsyncUnaryCall(Response(("ORR/A", "router/a", true))))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "billing down")));
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var provider = Build(client.Object, time);

        var first = await provider.ListActiveModelsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        var second = await provider.ListActiveModelsAsync(CancellationToken.None);

        first.Should().ContainSingle(m => m.ModelId == "ORR/A");
        second.Should().ContainSingle(m => m.ModelId == "ORR/A");
        client.Verify(c => c.ListModelsAsync(It.IsAny<ListModelsRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static GrpcActiveBillingModelProvider Build(
        BillingGrpc.BillingServiceClient client,
        TimeProvider? timeProvider = null)
    {
        return new(
            client,
            Options.Create(new CollectorOptions { ActiveModelsRefreshInterval = TimeSpan.FromMinutes(5) }).ToMonitor(),
            timeProvider ?? new MutableTimeProvider(DateTimeOffset.UnixEpoch),
            NullLogger<GrpcActiveBillingModelProvider>.Instance);
    }

    private static ListModelsResponse Response(params (string Id, string UpstreamId, bool Enabled)[] models)
    {
        var response = new ListModelsResponse();
        foreach (var model in models)
        {
            response.Models.Add(new ModelDetail
            {
                Id = model.Id,
                UpstreamModelId = model.UpstreamId,
                Enabled = model.Enabled
            });
        }

        return response;
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now += delta;
    }
}
