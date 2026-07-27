using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Application.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;
using OpenRussiaRouter.ControlPlane.Infrastructure.Workers;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterNodeSnapshotRefresherTests
{
    [Fact]
    public async Task RunOnce_ReplacesSnapshotAfterCompleteRead()
    {
        var repository = new InMemoryControlPlaneRepository();
        var node = new RouterNode("node-a", "node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        node.ApplyHealth(RouterNodeStatus.Healthy, DateTimeOffset.UtcNow);
        await repository.UpsertNodeAsync(node);
        var store = new RouterNodeSnapshotStore();
        var worker = CreateWorker(repository, store);

        await worker.RunOnceAsync(CancellationToken.None);

        store.GetSnapshot().Should().ContainSingle().Which.Id.Should().Be(node.Id);
    }

    [Fact]
    public async Task RunOnce_FailedRead_PreservesLastGoodSnapshot()
    {
        var store = new RouterNodeSnapshotStore();
        var node = new RouterNode("node-a", "node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        store.ReplaceAll([node]);
        var worker = CreateWorker(new ThrowingListRepository(), store);

        await ((Func<Task>)(() => worker.RunOnceAsync(CancellationToken.None))).Should().ThrowAsync<InvalidOperationException>();

        store.GetSnapshot().Should().ContainSingle().Which.Id.Should().Be(node.Id);
    }

    private static RouterNodeSnapshotRefresher CreateWorker(IControlPlaneRepository repository, RouterNodeSnapshotStore store) => new(
        repository,
        store,
        TestOptionsMonitor.Create(new RouterNodeSnapshotOptions()),
        NullLogger<RouterNodeSnapshotRefresher>.Instance);

    private sealed class ThrowingListRepository : IControlPlaneRepository
    {
        public Task UpsertNodeAsync(RouterNode node, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateNodeMetadataAsync(string nodeId, string name, Uri endpointUrl, string controlToken, int capacityWeight, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateNodeControlTokenAsync(string nodeId, string controlToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateNodeEnabledAsync(string nodeId, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RouterNode?> GetNodeAsync(string nodeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(IReadOnlyCollection<RouterNode> Items, bool HasMore)> ListNodesAsync(int limit, string? afterName = null, CancellationToken ct = default) => throw new InvalidOperationException("database unavailable");
        public Task<RouterNodeLease?> TryAcquireLeaseAsync(IReadOnlyCollection<string> candidateNodeIds, RouterLease lease, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ReleaseLeaseAsync(string leaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ReleaseExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ApplyHealthAsync(string nodeId, RouterNodeStatus status, DateTimeOffset checkedAtUtc, int? latencyMs, string? errorCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveIntegrationKeyMappingAsync(string apiKeyId, string nodeId, string routerKeyId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<(string NodeId, string RouterKeyId)>> GetIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveIntegrationKeyMappingsAsync(string apiKeyId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
