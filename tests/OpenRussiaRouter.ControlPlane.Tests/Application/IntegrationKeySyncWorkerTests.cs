using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;
using OpenRussiaRouter.ControlPlane.Application.Contracts;
using OpenRussiaRouter.ControlPlane.Application.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;
using OpenRussiaRouter.ControlPlane.Infrastructure.Workers;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

/// <summary>
/// The sweep exists because the per-creation push is best-effort: WebApp logs a failure and
/// still returns the key, so a node that was down at that moment never received it and kept
/// answering 401 forever. These tests pin the two properties that make the sweep useful —
/// it covers EVERY enabled node, and one failing node does not stop the rest.
/// </summary>
public sealed class IntegrationKeySyncWorkerTests
{
    [Fact]
    public async Task RunOnce_SyncsEveryEnabledNode()
    {
        var (controlPlane, provider) = await CreateControlPlaneWithNodesAsync(enabled: 2, disabled: 0);
        var worker = CreateWorker(controlPlane, provider);

        await worker.RunOnceAsync();

        provider.SyncedNodeIds.Should().HaveCount(2);
        provider.SyncedNodeIds.Should().BeEquivalentTo(provider.EnabledNodeIds);
    }

    [Fact]
    public async Task RunOnce_SkipsDisabledNodes()
    {
        var (controlPlane, provider) = await CreateControlPlaneWithNodesAsync(enabled: 1, disabled: 1);
        var worker = CreateWorker(controlPlane, provider);

        await worker.RunOnceAsync();

        provider.SyncedNodeIds.Should().ContainSingle().Which.Should().Be(provider.EnabledNodeIds.Single());
    }

    [Fact]
    public async Task RunOnce_OneNodeFailing_StillSyncsTheOthers()
    {
        // A single unreachable node must not cost the fleet its keys — that is the whole
        // point of a sweep rather than a single all-or-nothing call.
        var (controlPlane, provider) = await CreateControlPlaneWithNodesAsync(enabled: 3, disabled: 0);
        provider.FailForNodeName = "node-0";
        var worker = CreateWorker(controlPlane, provider);

        await worker.RunOnceAsync();

        provider.SuccessfulSyncs.Should().Be(2);
        provider.SyncedNodeIds.Should().HaveCount(3); // attempted all three
    }

    [Fact]
    public async Task RunOnce_Disabled_DoesNothing()
    {
        var (controlPlane, provider) = await CreateControlPlaneWithNodesAsync(enabled: 2, disabled: 0);
        var worker = CreateWorker(controlPlane, provider, new IntegrationKeySyncOptions { Enabled = false });

        await worker.RunOnceAsync();

        provider.SyncedNodeIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnce_PushesEveryNonDeletedKeyWithItsActiveState()
    {
        // Deactivated keys must travel too, carrying isActive=false: a key revoked while the
        // node was offline has to arrive revoked, not be silently omitted (omission means
        // "delete", and the node treats it that way only for keys it owns).
        var (controlPlane, provider) = await CreateControlPlaneWithNodesAsync(enabled: 1, disabled: 0);
        var worker = CreateWorker(controlPlane, provider);

        await worker.RunOnceAsync();

        provider.LastSyncedKeys.Should().HaveCount(2);
        provider.LastSyncedKeys.Should().Contain(k => k.Key == "sk-or-active" && k.IsActive);
        provider.LastSyncedKeys.Should().Contain(k => k.Key == "sk-or-revoked" && !k.IsActive);
    }

    [Fact]
    public async Task SyncService_UnknownNode_ReturnsNullInsteadOfThrowing()
    {
        var (controlPlane, provider) = await CreateControlPlaneWithNodesAsync(enabled: 1, disabled: 0);
        var service = CreateSyncService(controlPlane, provider);

        var result = await service.SyncNodeAsync("does-not-exist");

        result.Should().BeNull();
        provider.SyncedNodeIds.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncService_ProviderRejectsPayload_ReturnsNullAndDoesNotThrow()
    {
        // The production symptom was an opaque HTTP 500 from the node whose bulk endpoint
        // rolled back on a UNIQUE constraint. The service reports null; the caller decides.
        var (controlPlane, provider) = await CreateControlPlaneWithNodesAsync(enabled: 1, disabled: 0);
        provider.ThrowOnSync = new RouterNodeProviderException(
            System.Net.HttpStatusCode.InternalServerError,
            "Failed to sync integration keys",
            "Router provider request failed with HTTP 500: Failed to sync integration keys");
        var service = CreateSyncService(controlPlane, provider);

        var nodeId = provider.EnabledNodeIds.Single();
        var act = async () => await service.SyncNodeAsync(nodeId);

        (await act()).Should().BeNull();
    }

    private static IntegrationKeySyncWorker CreateWorker(
        RouterControlPlaneService controlPlane,
        RecordingProviderClient provider,
        IntegrationKeySyncOptions? options = null)
    {
        return new IntegrationKeySyncWorker(
            CreateSyncService(controlPlane, provider),
            TestOptionsMonitor.Create(options ?? new IntegrationKeySyncOptions()),
            NullLogger<IntegrationKeySyncWorker>.Instance);
    }

    private static IntegrationKeySyncService CreateSyncService(
        RouterControlPlaneService controlPlane,
        RecordingProviderClient provider)
    {
        return new IntegrationKeySyncService(
            controlPlane,
            provider,
            new StaticKeySource(),
            NullLogger<IntegrationKeySyncService>.Instance);
    }

    /// <summary>
    /// The authoritative key set the service would have read from AuthService: one active key
    /// and one revoked-but-not-deleted key, which must still travel.
    /// </summary>
    private sealed class StaticKeySource : IIntegrationKeySource
    {
        public Task<IReadOnlyList<RouterIntegrationKeySyncEntry>?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RouterIntegrationKeySyncEntry>?>(
                [
                    new RouterIntegrationKeySyncEntry { Name = "active", Key = "sk-or-active", IsActive = true },
                    new RouterIntegrationKeySyncEntry { Name = "revoked", Key = "sk-or-revoked", IsActive = false }
                ]);
    }

    private static async Task<(RouterControlPlaneService ControlPlane, RecordingProviderClient Provider)>
        CreateControlPlaneWithNodesAsync(int enabled, int disabled)
    {
        var controlPlane = new RouterControlPlaneService(
            new InMemoryControlPlaneRepository(),
            new NodeModelCapabilityStore());
        var provider = new RecordingProviderClient();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < enabled; i++)
        {
            var node = await controlPlane.RegisterNodeAsync(
                $"node-{i}", new Uri($"https://router-{i}.example"), $"secret-{i}", 1, now);
            await controlPlane.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, now);
            provider.EnabledNodeIds.Add(node.Id);
            provider.NodeNames[node.Id] = node.Name;
        }

        for (var i = 0; i < disabled; i++)
        {
            var node = await controlPlane.RegisterNodeAsync(
                $"disabled-{i}", new Uri($"https://disabled-{i}.example"), $"secret-d{i}", 1, now);
            await controlPlane.DisableNodeAsync(node.Id);
            provider.NodeNames[node.Id] = node.Name;
        }

        return (controlPlane, provider);
    }

    /// <summary>
    /// Stands in for the node's admin API. Only the sync path is exercised here; every other
    /// member throws so an accidental call is loud rather than silently green.
    /// </summary>
    private sealed class RecordingProviderClient : IRouterNodeProvider
    {
        public List<string> SyncedNodeIds { get; } = [];
        public List<string> EnabledNodeIds { get; } = [];
        public Dictionary<string, string> NodeNames { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<RouterIntegrationKeySyncEntry> LastSyncedKeys { get; private set; } = [];
        public string? FailForNodeName { get; set; }
        public Exception? ThrowOnSync { get; set; }
        public int SuccessfulSyncs { get; private set; }

        public Task<RouterIntegrationKeysSyncResponse> SyncIntegrationKeysAsync(
            RouterNode node,
            IReadOnlyList<RouterIntegrationKeySyncEntry> keys,
            CancellationToken cancellationToken)
        {
            SyncedNodeIds.Add(node.Id);
            LastSyncedKeys = keys;

            if (ThrowOnSync is not null)
                throw ThrowOnSync;

            if (FailForNodeName is not null && node.Name == FailForNodeName)
            {
                throw new RouterNodeProviderException(
                    System.Net.HttpStatusCode.InternalServerError,
                    "boom",
                    "Router provider request failed with HTTP 500: boom");
            }

            SuccessfulSyncs++;
            return Task.FromResult(new RouterIntegrationKeysSyncResponse
            {
                AddedCount = SyncedNodeIds.Count,
                DisabledCount = 0,
                ReactivatedCount = 0,
                DeletedCount = 0
            });
        }

        public Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
            RouterNode node, bool includeInactive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
            RouterNode node, string connectionId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeysResponse> ListIntegrationKeysAsync(
            RouterNode node, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
            RouterNode node, string name, string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
            RouterNode node, string keyId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RouterIntegrationKeyDeleteResponse> DeleteIntegrationKeyAsync(
            RouterNode node, string keyId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
