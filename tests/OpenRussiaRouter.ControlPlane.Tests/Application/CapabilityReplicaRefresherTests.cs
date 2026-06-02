using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public class CapabilityReplicaRefresherTests
{
    private static IOptionsMonitor<CapabilityReplicaOptions> Opts() =>
        new StaticMonitor<CapabilityReplicaOptions>(new CapabilityReplicaOptions());

    private static AsyncUnaryCall<T> MakeAsyncUnaryCall<T>(T response)
        => new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    [Fact]
    public async Task RunOnceAsync_PopulatesStoreFromCollectorSnapshot()
    {
        var response = new GetCapabilitiesSnapshotResponse { CollectedAtUtc = "x" };
        var cap = new NodeCapability { NodeId = "n1" };
        cap.ModelIds.AddRange(["gpt", "claude"]);
        response.Nodes.Add(cap);

        var client = new Mock<Collector.CollectorClient>();
        client.Setup(c => c.GetCapabilitiesSnapshotAsync(
                It.IsAny<GetCapabilitiesSnapshotRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(MakeAsyncUnaryCall(response));

        var store = new NodeModelCapabilityStore();
        var refresher = new CapabilityReplicaRefresher(client.Object, store, Opts(), NullLogger<CapabilityReplicaRefresher>.Instance);

        await refresher.RunOnceAsync(CancellationToken.None);

        store.NodeSupportsModel("n1", "gpt").Should().BeTrue();
        store.NodeSupportsModel("n1", "claude").Should().BeTrue();
        store.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task RunOnceAsync_KeepsExistingStore_OnCollectorError()
    {
        var client = new Mock<Collector.CollectorClient>();
        client.Setup(c => c.GetCapabilitiesSnapshotAsync(
                It.IsAny<GetCapabilitiesSnapshotRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "down")));

        var store = new NodeModelCapabilityStore();
        store.SetNodeModels("old", new HashSet<string> { "m" });
        var refresher = new CapabilityReplicaRefresher(client.Object, store, Opts(), NullLogger<CapabilityReplicaRefresher>.Instance);

        await refresher.RunOnceAsync(CancellationToken.None);

        store.NodeSupportsModel("old", "m").Should().BeTrue();
    }

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T> where T : class
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
