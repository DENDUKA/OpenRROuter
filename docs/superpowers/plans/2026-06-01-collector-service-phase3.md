# Collector Service — Phase 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut consumers over to the CollectorService and remove the now-duplicated collection code: ControlPlane feeds its `NodeModelCapabilityStore` from the collector (replacing the Quartz job) and drops `ProviderQuotaCollectorWorker`; WebApp reads the node-model catalog, per-node models, and OpenRouter catalog from the collector; the three catalog RPCs are removed from `control-plane.proto`.

**Architecture:** ControlPlane becomes a collector gRPC client with a background `CapabilityReplicaRefresher` that periodically pulls `GetCapabilitiesSnapshot` and updates the in-memory `NodeModelCapabilityStore` used in the routing hot path — latency unchanged. WebApp gains a `CollectorProvider` gRPC client and four call sites move from `ControlPlaneProvider` to it. The live catalog-collection logic and the Quartz job are deleted from ControlPlane.

**Tech Stack:** Same as Phases 1-2.

**Prerequisite:** Phases 1 and 2 merged and deployed (the collector must be running and serving snapshots before ControlPlane/WebApp depend on it). **Spec:** `docs/superpowers/specs/2026-06-01-collector-service-design.md`.

---

## Design decision (deviation from spec, flagged for review)

The spec lists `CachedRouterNodeProvider` / `RouterNodeProviderCacheStore` for removal "as redundant." That holds for the **catalog/models** reads, which move to the collector. But `GrpcRouterControlPlaneService.GetRouterNodeDetails` still does **live** quota + integration-key reads, and key push/revoke still writes — all via `IRouterNodeProvider`. Those paths remain in ControlPlane and still benefit from the short TTL cache.

**This plan therefore keeps `RouterNodeProvider` + `CachedRouterNodeProvider` for the quota/keys/admin paths, and removes only the model/catalog methods that moved** (`ListModelsAsync`, `ListProvidersWithModelsAsync`, `DescribeListModelsRequest`, `DescribeListProvidersRequest` from the interface and both implementations). This keeps admin-page behavior identical and is lower risk. If a future cleanup proves the cache no longer earns its keep for quota/keys, remove it then.

---

## File Structure

**Contract (modify):** `src/OpenRussiaRouter.Contracts/Protos/control-plane.proto` — remove `ListAvailableNodeModels`, `ListNodeModelCatalog`, `GetNodeModels` RPCs + their messages.

**ControlPlane (modify/delete):**
- `OpenRussiaRouter.ControlPlane.csproj` — add `collector.proto` as `Client`.
- `Application/Services/CapabilityReplicaRefresher.cs` (new) + `CapabilityReplicaOptions.cs` (new).
- `Application/Extensions/ControlPlaneExtensions.cs` — register collector client + refresher; remove Quartz job + quota worker registrations.
- `Infrastructure/Startup/Startup.cs` — register collector gRPC client.
- `Infrastructure/Grpc/GrpcRouterControlPlaneService.cs` — delete the three catalog RPC overrides + catalog-collection helpers.
- Delete: `Application/Services/RouterModelCapabilityJob.cs`, `Application/Services/RouterModelCapabilityOptions.cs`, `Application/Metrics/ProviderQuotaCollectorWorker.cs`.
- `Application/Services/IRouterNodeProvider.cs`, `Infrastructure/Http/RouterNodeProvider.cs`, `Infrastructure/Http/CachedRouterNodeProvider.cs` — drop the model/catalog methods.

**WebApp (modify/new):**
- `OpenRussiaRouter.WebApp.csproj` — add `collector.proto` as `Client`.
- `Program.cs` — register the collector gRPC client + `CollectorProvider`.
- `Services/Collector/CollectorProvider.cs` (new) — wraps the collector client; provides `ListNodeModelCatalogAsync`, `GetNodeModelsAsync`, `ListAvailableNodeModelsAsync`.
- `Services/Enrichment/OpenRouterCatalogClient.cs` — `LoadCatalogAsync` now calls the collector.
- Call sites: `Pages/Models.cshtml.cs`, `Pages/Admin/ModelsSync.cshtml.cs`, `Pages/Admin/Nodes.cshtml.cs`, `Pages/Admin/Models.cshtml.cs`.
- `Services/ControlPlane/ControlPlaneProvider.cs` — remove the three migrated methods.

**Docs:** `CLAUDE.md` — add the collector to the service table; update the migration/status notes.

---

## Task 1: ControlPlane — capability replica refresher

This replaces the Quartz `RouterModelCapabilityJob`. The refresher pulls `GetCapabilitiesSnapshot` from the collector and updates `NodeModelCapabilityStore` (used in the routing hot path).

**Files:**
- Modify: `src/OpenRussiaRouter.ControlPlane/OpenRussiaRouter.ControlPlane.csproj`
- Create: `src/OpenRussiaRouter.ControlPlane/Application/Services/CapabilityReplicaOptions.cs`
- Create: `src/OpenRussiaRouter.ControlPlane/Application/Services/CapabilityReplicaRefresher.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Application/Services/NodeModelCapabilityStore.cs`
- Test: `tests/OpenRussiaRouter.ControlPlane.Tests/Application/CapabilityReplicaRefresherTests.cs`

- [ ] **Step 1: Reference collector.proto from ControlPlane**

In `OpenRussiaRouter.ControlPlane.csproj`, add next to the existing `<Protobuf>` lines:

```xml
<Protobuf Include="..\OpenRussiaRouter.Contracts\Protos\collector.proto" GrpcServices="Client" Link="Protos\collector.proto" />
```

- [ ] **Step 2: Add a `ReplaceAll` method to the capability store**

`NodeModelCapabilityStore` currently only has `SetNodeModels` per node. The refresher needs to replace the whole map atomically (so deleted nodes drop out). Add to `NodeModelCapabilityStore.cs`:

```csharp
    /// <summary>Atomically replaces the entire node→models map with the given snapshot.</summary>
    public void ReplaceAll(IReadOnlyDictionary<string, IReadOnlySet<string>> nodeModels)
    {
        ArgumentNullException.ThrowIfNull(nodeModels);
        _nodeModels.Clear();
        foreach (var (nodeId, models) in nodeModels)
            _nodeModels[nodeId] = models;
    }
```

> `ConcurrentDictionary.Clear()` + writes is not a single atomic operation, but `NodeSupportsModel` reads are per-key and tolerate a brief in-progress rebuild; this matches the existing per-node `SetNodeModels` semantics and is acceptable for a routing hint.

- [ ] **Step 3: Create options**

`src/OpenRussiaRouter.ControlPlane/Application/Services/CapabilityReplicaOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class CapabilityReplicaOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
}
```

- [ ] **Step 4: Write the failing test**

`tests/OpenRussiaRouter.ControlPlane.Tests/Application/CapabilityReplicaRefresherTests.cs`:

```csharp
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
            .Returns(TestCalls.AsyncUnaryCall(response));

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
```

> `TestCalls.AsyncUnaryCall` is in `Grpc.Core.Testing` (package `Grpc.Core.Api`, already transitively available via Grpc.Net.Client). If unavailable, build the `AsyncUnaryCall<T>` manually: `new AsyncUnaryCall<T>(Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { })`.

- [ ] **Step 5: Run to verify it fails**

Run: `dotnet test tests/OpenRussiaRouter.ControlPlane.Tests --filter CapabilityReplicaRefresherTests -nr:false -m:1`
Expected: FAIL — `CapabilityReplicaRefresher` does not exist.

- [ ] **Step 6: Implement the refresher**

`src/OpenRussiaRouter.ControlPlane/Application/Services/CapabilityReplicaRefresher.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Periodically pulls the model-capability snapshot from the CollectorService and updates the
/// local <see cref="NodeModelCapabilityStore"/> used by the routing hot path. Replaces the
/// in-process Quartz polling job; routing latency is unchanged because reads stay local.
/// </summary>
public sealed class CapabilityReplicaRefresher(
    Collector.CollectorClient collectorClient,
    NodeModelCapabilityStore store,
    IOptionsMonitor<CapabilityReplicaOptions> options,
    ILogger<CapabilityReplicaRefresher> logger)
    : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
            return;

        GetCapabilitiesSnapshotResponse response;
        try
        {
            response = await collectorClient
                .GetCapabilitiesSnapshotAsync(new GetCapabilitiesSnapshotRequest(), cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the existing store on a transient collector outage — routing keeps using stale hints.
            logger.LogWarning(ex, "Capability snapshot refresh from collector failed; keeping existing replica");
            return;
        }

        var map = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var node in response.Nodes)
            map[node.NodeId] = node.ModelIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        store.ReplaceAll(map);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Capability replica refresh cycle failed");
            }

            try
            {
                await Task.Delay(options.CurrentValue.RefreshInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
```

- [ ] **Step 7: Run to verify it passes**

Run: `dotnet test tests/OpenRussiaRouter.ControlPlane.Tests --filter CapabilityReplicaRefresherTests -nr:false -m:1`
Expected: PASS (after Task 2's wiring compiles — if the collector client isn't yet registered, the test still compiles because it constructs the refresher directly with a mock client).

- [ ] **Step 8: Commit**

```bash
git add src/OpenRussiaRouter.ControlPlane/OpenRussiaRouter.ControlPlane.csproj src/OpenRussiaRouter.ControlPlane/Application/Services/CapabilityReplicaOptions.cs src/OpenRussiaRouter.ControlPlane/Application/Services/CapabilityReplicaRefresher.cs src/OpenRussiaRouter.ControlPlane/Application/Services/NodeModelCapabilityStore.cs tests/OpenRussiaRouter.ControlPlane.Tests/Application/CapabilityReplicaRefresherTests.cs
git commit -m "feat(control-plane): add capability replica refresher fed by collector"
```

---

## Task 2: ControlPlane — register collector client + refresher, drop Quartz + quota worker

**Files:**
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Startup/Startup.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Application/Extensions/ControlPlaneExtensions.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Program.cs` (no metric meter change needed)

- [ ] **Step 1: Register the collector gRPC client**

In `Startup.ConfigureServices`, after the options registrations add:

```csharp
        services.AddOptions<CapabilityReplicaOptions>()
            .Bind(configuration.GetSection("CapabilityReplica"))
            .ValidateDataAnnotations();

        var grpcCallDeadline = TimeSpan.FromSeconds(
            configuration.GetValue("GrpcServices:CallDeadlineSeconds", 30));
        var collectorUrl = configuration["GrpcServices:Collector"] ?? "http://localhost:5005";
        services.AddGrpcClient<OpenRussiaRouter.Contracts.Grpc.Collector.V1.Collector.CollectorClient>(
                o => o.Address = new Uri(collectorUrl))
            .ConfigureChannel(o =>
            {
                o.HttpHandler = new SocketsHttpHandler
                {
                    PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                };
            })
            .AddInterceptor(() => new OpenRussiaRouter.BuildingBlocks.Grpc.GrpcDeadlineInterceptor(grpcCallDeadline));
```

(Add `using OpenRussiaRouter.ControlPlane.Application.Services;` if not already present; it is.)

- [ ] **Step 2: Register the refresher and remove the Quartz job + quota worker**

In `ControlPlaneExtensions.AddControlPlaneApplicationServices`:

- Delete the `ProviderQuotaCollectorWorker` block (the `AddHttpClient<ProviderQuotaCollectorWorker>()`, `AddOptions<ProviderQuotaCollectorOptions>()`, and `AddHostedService<ProviderQuotaCollectorWorker>()` lines).
- Delete the entire Quartz block: the `AddOptions<RouterModelCapabilityOptions>()` registration, the `services.AddQuartz(...)` call, and `services.AddQuartzHostedService(...)`.
- Add: `services.AddHostedService<CapabilityReplicaRefresher>();`

After edits the method keeps: `NodeModelCapabilityStore`, `RouterControlPlaneService`, the health provider, `RouterNodeProviderCacheStore`, `RouterNodeProvider`, `CachedRouterNodeProvider`, `RouterHealthCheckWorker`, `RouterLeaseCleanupWorker`, and the new `CapabilityReplicaRefresher`. Remove the now-unused `using Quartz;` and `using OpenRussiaRouter.ControlPlane.Application.Metrics;` if they become unused.

- [ ] **Step 3: Remove the Quartz package reference**

In `OpenRussiaRouter.ControlPlane.csproj`, remove the `Quartz` and `Quartz.Extensions.Hosting` `<PackageReference>` lines.

- [ ] **Step 4: Delete the dead files**

Delete:
- `src/OpenRussiaRouter.ControlPlane/Application/Services/RouterModelCapabilityJob.cs`
- `src/OpenRussiaRouter.ControlPlane/Application/Services/RouterModelCapabilityOptions.cs`
- `src/OpenRussiaRouter.ControlPlane/Application/Metrics/ProviderQuotaCollectorWorker.cs`

```bash
git rm src/OpenRussiaRouter.ControlPlane/Application/Services/RouterModelCapabilityJob.cs src/OpenRussiaRouter.ControlPlane/Application/Services/RouterModelCapabilityOptions.cs src/OpenRussiaRouter.ControlPlane/Application/Metrics/ProviderQuotaCollectorWorker.cs
```

- [ ] **Step 5: Add config defaults**

In `src/OpenRussiaRouter.ControlPlane/appsettings.json`, add `GrpcServices:Collector` and a `CapabilityReplica` section:

```json
  "GrpcServices": {
    "Collector": "http://localhost:5005"
  },
  "CapabilityReplica": {
    "Enabled": true,
    "RefreshInterval": "00:01:00"
  }
```

(Merge into the existing `GrpcServices` block rather than duplicating it.)

- [ ] **Step 6: Build (expect catalog-RPC errors next task)**

Run: `dotnet build src/OpenRussiaRouter.ControlPlane/OpenRussiaRouter.ControlPlane.csproj -nr:false -m:1`
Expected: compile errors only from the still-present catalog RPC overrides referencing removed things — these are fixed in Task 3. (If the build is clean here, even better.)

- [ ] **Step 7: Commit**

```bash
git add src/OpenRussiaRouter.ControlPlane/
git commit -m "feat(control-plane): wire collector client + refresher, drop Quartz and quota worker"
```

---

## Task 3: ControlPlane — remove catalog RPCs + collection helpers from proto and service

**Files:**
- Modify: `src/OpenRussiaRouter.Contracts/Protos/control-plane.proto`
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Grpc/GrpcRouterControlPlaneService.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Application/Services/IRouterNodeProvider.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Http/RouterNodeProvider.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Http/CachedRouterNodeProvider.cs`
- Modify: `tests/OpenRussiaRouter.ControlPlane.Tests/Application/GrpcRouterControlPlaneServiceTests.cs`

- [ ] **Step 1: Remove the three RPCs + messages from `control-plane.proto`**

In `control-plane.proto`, delete these `service` lines:

```proto
  rpc ListAvailableNodeModels (ListAvailableNodeModelsRequest) returns (ListAvailableNodeModelsResponse);
  rpc ListNodeModelCatalog (ListNodeModelCatalogRequest) returns (ListNodeModelCatalogResponse);
  rpc GetNodeModels (GetNodeModelsRequest) returns (GetNodeModelsResponse);
```

And delete the now-unused messages: `ListAvailableNodeModelsRequest`, `ListAvailableNodeModelsResponse`, `ListNodeModelCatalogRequest`, `UpstreamModelEntry`, `UnreachableNodeInfo`, `ListNodeModelCatalogResponse`, `GetNodeModelsRequest`, `GetNodeModelsResponse`.

- [ ] **Step 2: Remove the overrides + helpers from `GrpcRouterControlPlaneService.cs`**

Delete these members:
- `ListAvailableNodeModels` override (≈ lines 329-353)
- `GetNodeModels` override (≈ 355-376)
- `ListNodeModelCatalog` override (≈ 378-452)
- `NodeCatalogPerNodeTimeout` field, `CollectNodeCatalogAsync`, `CollectFallbackModelsAsync`, `IsTransientProviderError`, `IsRoutableForCatalog`, `FormatRequest`, `FormatResponse`, `ChooseField`, `GetRoutableModelId` (≈ 454-610)
- the private `NodeCatalogResult` record, `ModelObservation` class, `CatalogAccumulator` class (≈ 612-640)

Keep `MapNodeSummary` (≈ 642-654) and everything quota/keys-related. After this, `providerClient` is only used for `GetProviderQuotaAsync`, `ListIntegrationKeysAsync`, `CreateIntegrationKeyAsync`, `UpdateIntegrationKeyStatusAsync` (and `UpdateProviderStatusAsync` if present). Remove the `using System.Net;` and `using System.Net.Http;` imports only if they become unused.

- [ ] **Step 3: Drop the model/catalog methods from the provider interface + implementations**

In `IRouterNodeProvider.cs`, remove:
```csharp
    string DescribeListProvidersRequest(RouterNode node);
    string DescribeListModelsRequest(RouterNode node);
    Task<NodeModelsResponse> ListModelsAsync(RouterNode node, CancellationToken cancellationToken);
    Task<RouterProvidersWithModelsResponse> ListProvidersWithModelsAsync(RouterNode node, CancellationToken cancellationToken);
```

In `RouterNodeProvider.cs`, remove the matching method bodies (`DescribeListProvidersRequest`, `DescribeListModelsRequest`, `ListModelsAsync`, `ListProvidersWithModelsAsync`) and the now-unused `ModelsPath` / `ProvidersWithModelsPath` constants and the `DescribeRequest` helper if unused.

In `CachedRouterNodeProvider.cs`, remove the `DescribeListProvidersRequest`, `DescribeListModelsRequest`, `ListModelsAsync`, and `ListProvidersWithModelsAsync` overrides (the `"models"` and `"providers-with-models"` cache operations). Keep quota + integration-keys caching.

> The contract DTOs `NodeModelsResponse`, `NodeModelInfo`, `RouterProvidersWithModelsResponse`, `RouterProviderWithModels`, `RouterProviderModelInfo` in `RouterNodeProviderContracts.cs` become unused in ControlPlane; delete them too, or leave them (harmless). Recommended: delete to avoid dead code.

- [ ] **Step 4: Fix the ControlPlane tests**

In `tests/OpenRussiaRouter.ControlPlane.Tests/Application/GrpcRouterControlPlaneServiceTests.cs`, delete every test referencing the removed RPCs: all `ListNodeModelCatalog_*`, `GetNodeModels_*`, and any `ListAvailableNodeModels` tests (grep the file for `ListNodeModelCatalog`, `GetNodeModels`, `ListAvailableNodeModels`). Also remove any mock setups for the now-removed `IRouterNodeProvider` model/catalog methods in the surviving tests (e.g. `ListProvidersWithModelsAsync`/`ListModelsAsync` setups) — the surviving tests cover select/release/register/quota/keys.

- [ ] **Step 5: Build + run ControlPlane tests**

Run:
```bash
dotnet build src/OpenRussiaRouter.ControlPlane/OpenRussiaRouter.ControlPlane.csproj -nr:false -m:1
dotnet test tests/OpenRussiaRouter.ControlPlane.Tests -nr:false -m:1
```
Expected: build succeeds; ControlPlane tests pass (minus the deleted ones).

- [ ] **Step 6: Commit**

```bash
git add src/OpenRussiaRouter.Contracts/Protos/control-plane.proto src/OpenRussiaRouter.ControlPlane/ tests/OpenRussiaRouter.ControlPlane.Tests/
git commit -m "refactor(control-plane): remove catalog RPCs and live collection (moved to collector)"
```

---

## Task 4: WebApp — collector gRPC client + provider

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj`
- Modify: `src/OpenRussiaRouter.WebApp/Program.cs`
- Create: `src/OpenRussiaRouter.WebApp/Services/Collector/CollectorProvider.cs`

- [ ] **Step 1: Reference collector.proto in WebApp**

In `OpenRussiaRouter.WebApp.csproj`, add next to the existing `<Protobuf>` lines:

```xml
<Protobuf Include="..\OpenRussiaRouter.Contracts\Protos\collector.proto" GrpcServices="Client" Link="Protos\collector.proto" />
```

- [ ] **Step 2: Register the collector client + provider in Program.cs**

After the ControlPlane client registration block (around line 113, `builder.Services.AddScoped<ControlPlaneProvider>();`), add:

```csharp
var collectorGrpcUrl = builder.Configuration["GrpcServices:Collector"] ?? "http://localhost:5005";
builder.Services.AddGrpcClient<OpenRussiaRouter.Contracts.Grpc.Collector.V1.Collector.CollectorClient>(options =>
{
    options.Address = new Uri(collectorGrpcUrl);
})
.ConfigureChannel(options =>
{
    options.HttpHandler = new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
    };
})
.AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));
builder.Services.AddScoped<OpenRussiaRouter.WebApp.Services.Collector.CollectorProvider>();
```

- [ ] **Step 3: Implement `CollectorProvider`**

`src/OpenRussiaRouter.WebApp/Services/Collector/CollectorProvider.cs` — reuses the existing WebApp result types `NodeModelCatalogResult`, `NodeModelCatalogEntry`, `UnreachableNodeDetail` (defined in `Services/ControlPlane/`; reference their namespace). Mirrors the timeouts in the old `ControlPlaneProvider` methods:

```csharp
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;
using OpenRussiaRouter.WebApp.Services.ControlPlane; // NodeModelCatalogResult, NodeModelCatalogEntry, UnreachableNodeDetail

namespace OpenRussiaRouter.WebApp.Services.Collector;

public sealed class CollectorProvider(
    Collector.CollectorClient client,
    ILogger<CollectorProvider> logger)
{
    private static readonly TimeSpan NodeModelCatalogTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan NodeModelDropdownTimeout = TimeSpan.FromSeconds(8);

    public async Task<NodeModelCatalogResult> ListNodeModelCatalogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(NodeModelCatalogTimeout);
            var response = await client.ListNodeModelCatalogAsync(
                new ListNodeModelCatalogRequest(), cancellationToken: timeoutCts.Token);

            return new NodeModelCatalogResult(
                Entries: response.Entries.Select(e => new NodeModelCatalogEntry(
                    UpstreamId: e.UpstreamId,
                    PublicId: e.PublicId,
                    DisplayName: e.DisplayName,
                    ModelType: e.ModelType,
                    SourceProviders: e.SourceProviders.ToList(),
                    NodeIds: e.NodeIds.ToList())).ToList(),
                UnreachableNodeIds: response.UnreachableNodeIds.ToList(),
                UnreachableNodes: response.UnreachableNodes.Select(u => new UnreachableNodeDetail(
                    NodeId: u.NodeId, Request: u.Request, Response: u.Response)).ToList(),
                Error: null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load node model catalog from collector");
            return new NodeModelCatalogResult([], [], [], $"Failed to load node model catalog: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> GetNodeModelsAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(NodeModelDropdownTimeout);
            var response = await client.GetNodeModelsAsync(
                new GetNodeModelsRequest { NodeId = nodeId ?? string.Empty }, cancellationToken: timeoutCts.Token);
            return string.IsNullOrWhiteSpace(response.Error) ? response.PublicIds.ToList() : [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get models for node {NodeId} from collector", nodeId);
            return [];
        }
    }

    /// <summary>Distinct routable model ids across all nodes — derived from the catalog entries.</summary>
    public async Task<IReadOnlyList<string>> ListAvailableNodeModelsAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await ListNodeModelCatalogAsync(cancellationToken);
        return catalog.Entries
            .Select(e => e.PublicId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }
}
```

> Confirm the exact namespace of `NodeModelCatalogResult` / `NodeModelCatalogEntry` / `UnreachableNodeDetail` by opening `src/OpenRussiaRouter.WebApp/Services/ControlPlane/ControlPlaneProvider.cs` (they are declared in that folder). Adjust the `using` if needed.

- [ ] **Step 4: Build WebApp**

Run: `dotnet build src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj -nr:false -m:1`
Expected: Build succeeded (call sites still use `ControlPlaneProvider` — updated in Task 5).

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj src/OpenRussiaRouter.WebApp/Program.cs src/OpenRussiaRouter.WebApp/Services/Collector/CollectorProvider.cs
git commit -m "feat(webapp): add collector gRPC client and CollectorProvider"
```

---

## Task 5: WebApp — move call sites to the collector

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Models.cshtml.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Admin/ModelsSync.cshtml.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Admin/Nodes.cshtml.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Admin/Models.cshtml.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Services/ControlPlane/ControlPlaneProvider.cs`

- [ ] **Step 1: Public Models page**

In `Pages/Models.cshtml.cs`, add a `CollectorProvider` constructor parameter and change the catalog fetch (line ~78) from `ControlPlaneProvider.ListNodeModelCatalogAsync(...)` to `CollectorProvider.ListNodeModelCatalogAsync(...)`. The page already caches the result in `IMemoryCache` for 5 min — keep that. Add `using OpenRussiaRouter.WebApp.Services.Collector;`.

- [ ] **Step 2: Admin ModelsSync page**

In `Pages/Admin/ModelsSync.cshtml.cs`, add a `CollectorProvider` constructor parameter and change both `ControlPlaneProvider.ListNodeModelCatalogAsync(...)` calls (lines ~49, ~206) to `CollectorProvider.ListNodeModelCatalogAsync(...)`.

- [ ] **Step 3: Admin Nodes page**

In `Pages/Admin/Nodes.cshtml.cs`, add a `CollectorProvider` constructor parameter and change `ControlPlaneProvider.GetNodeModelsAsync(...)` (line ~101) to `CollectorProvider.GetNodeModelsAsync(...)`.

- [ ] **Step 4: Admin Models page**

In `Pages/Admin/Models.cshtml.cs`, add a `CollectorProvider` parameter (the page already injects `ControlPlaneProvider controlPlaneProvider`; add `CollectorProvider collectorProvider` and store it). Change `LoadAvailableNodeModels` (line ~358) from `_controlPlaneClient.ListAvailableNodeModelsAsync()` to the collector provider's `ListAvailableNodeModelsAsync()`.

- [ ] **Step 5: Remove the migrated methods from `ControlPlaneProvider`**

In `Services/ControlPlane/ControlPlaneProvider.cs`, delete `ListAvailableNodeModelsAsync` (≈ 152-169), `GetNodeModelsAsync` (≈ 171-191), and `ListNodeModelCatalogAsync` (≈ 193-228). Keep `MapNode` and the node CRUD / details methods. Keep the result types `NodeModelCatalogResult` / `NodeModelCatalogEntry` / `UnreachableNodeDetail` (now used by `CollectorProvider`) — if they live in this file, leave them in place.

- [ ] **Step 6: Build + run WebApp-affecting tests**

Run:
```bash
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test tests/OpenRussiaRouter.E2ETests --no-build -nr:false -m:1
```
Expected: solution builds. E2E tests are opt-in/skip without a running stack; at minimum the build must succeed. If E2E is not configured locally, rely on the build + manual smoke (Task 7).

- [ ] **Step 7: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/
git commit -m "refactor(webapp): read node catalog and models from collector"
```

---

## Task 6: WebApp — OpenRouter catalog via collector

`OpenRouterCatalogClient.FindMatch` stays. Only `LoadCatalogAsync` changes — it now reads the collector's cached catalog instead of calling `openrouter.ai` directly.

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Services/Enrichment/OpenRouterCatalogClient.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Program.cs`

- [ ] **Step 1: Change the client to take the collector client**

In `OpenRouterCatalogClient.cs`, change the primary constructor from `(HttpClient httpClient, ILogger<OpenRouterCatalogClient> logger)` to `(Collector.CollectorClient collectorClient, ILogger<OpenRouterCatalogClient> logger)` and replace `LoadCatalogAsync` with a call to the collector, mapping the proto entries to the existing `OpenRouterModelEntry` shape (so `FindMatch` is unchanged):

```csharp
    public async Task<IReadOnlyList<OpenRouterModelEntry>> LoadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await collectorClient.GetOpenRouterCatalogAsync(
                new Contracts.Grpc.Collector.V1.GetOpenRouterCatalogRequest(),
                cancellationToken: cancellationToken);

            if (!response.Available)
                return [];

            return response.Entries.Select(e => new OpenRouterModelEntry(
                Slug: e.Slug,
                Name: string.IsNullOrEmpty(e.Name) ? null : e.Name,
                ShortName: null,
                AuthorDisplayName: string.IsNullOrEmpty(e.AuthorDisplayName) ? null : e.AuthorDisplayName,
                Description: string.IsNullOrEmpty(e.Description) ? null : e.Description,
                ContextLength: e.ContextLength > 0 ? e.ContextLength : null,
                InputModalities: e.InputModalities.ToList(),
                Endpoint: new OpenRouterEndpoint(new OpenRouterPricing(
                    Prompt: string.IsNullOrEmpty(e.PricePrompt) ? null : e.PricePrompt,
                    Completion: string.IsNullOrEmpty(e.PriceCompletion) ? null : e.PriceCompletion,
                    InputCacheRead: string.IsNullOrEmpty(e.PriceInputCacheRead) ? null : e.PriceInputCacheRead)))).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load OpenRouter catalog from collector");
            return [];
        }
    }
```

Add `using OpenRussiaRouter.Contracts.Grpc.Collector.V1;` and keep the existing `FindMatch` and all helper methods unchanged.

- [ ] **Step 2: Change the DI registration**

In `Program.cs`, replace the `AddHttpClient<IOpenRouterCatalogClient, OpenRouterCatalogClient>(...)` registration (≈ line 149-152, which set `BaseAddress = https://openrouter.ai/`) with a plain scoped registration that receives the already-registered collector client:

```csharp
builder.Services.AddScoped<IOpenRouterCatalogClient, OpenRouterCatalogClient>();
```

(The collector client is registered in Task 4 Step 2, so `OpenRouterCatalogClient` resolves it via DI.)

- [ ] **Step 3: Build**

Run: `dotnet build src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj -nr:false -m:1`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Services/Enrichment/OpenRouterCatalogClient.cs src/OpenRussiaRouter.WebApp/Program.cs
git commit -m "refactor(webapp): load OpenRouter catalog from collector"
```

---

## Task 7: Infra config + docs + final verification

**Files:**
- Modify: `docker-compose.yml`, `infra/kustomize/base/services/control-plane.yaml`, `infra/kustomize/base/services/webapp.yaml`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Point ControlPlane + WebApp at the collector in infra**

In `docker-compose.yml`, add `GrpcServices__Collector: "http://collector:8080"` to both the `control-plane` and `webapp` environment blocks.

In `infra/kustomize/base/services/control-plane.yaml` and `webapp.yaml`, add an env var:
```yaml
            - name: GrpcServices__Collector
              value: "http://collector:8080"
```

- [ ] **Step 2: Update CLAUDE.md**

In `CLAUDE.md`:
- Add a `Collector` row to the **Services** table: `| **CollectorService** | Periodic polling of nodes/OpenRouter, in-memory cache, serves snapshots | gRPC 5005 |`.
- In the "Готово/Не готово" status, note that capability collection, node-model catalog, provider quota, and OpenRouter catalog now run in the CollectorService; ControlPlane consumes a capability replica.
- Remove/adjust references to `RouterModelCapabilityJob` and `ProviderQuotaCollectorWorker` living in ControlPlane.

- [ ] **Step 3: Validate infra**

Run:
```bash
docker compose config -q
kubectl kustomize infra/kustomize/base > $null
```
Expected: both succeed. (Skip if docker/kubectl unavailable.)

- [ ] **Step 4: Full build + full test**

Run:
```bash
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```
Expected: solution builds; all tests pass.

- [ ] **Step 5: Manual smoke (recommended)**

Start ControlPlane, Collector, then WebApp (`dotnet run --project ...` each). Register a node in the admin UI. Within one refresh interval:
- ControlPlane logs the capability replica refresh; routing still selects nodes by capability.
- WebApp `/models` and `/admin/models/sync` show the node catalog (served by the collector).
- `/admin/models` enrich pulls OpenRouter data (served by the collector).
- ControlPlane no longer logs `RouterModelCapabilityJob` or `ProviderQuotaCollector` output; the collector exposes `orr_provider_quota` at its `/metrics`.

- [ ] **Step 6: Commit**

```bash
git add docker-compose.yml infra/kustomize/base/services/control-plane.yaml infra/kustomize/base/services/webapp.yaml CLAUDE.md
git commit -m "build(collector): point consumers at collector; update docs"
```

---

## Self-Review Notes

- **Spec coverage (Phase 3 slice):** ControlPlane capability replica fed from collector replacing the Quartz job (Tasks 1-2); `ProviderQuotaCollectorWorker` removed (Task 2); `ListNodeModelCatalog`/`GetNodeModels`/`ListAvailableNodeModels` removed from `control-plane.proto` with WebApp reading the collector directly (Tasks 3-5); OpenRouter catalog read from collector with `FindMatch` retained in WebApp (Task 6); CLAUDE.md service table updated (Task 7).
- **Flagged deviation:** `CachedRouterNodeProvider`/`RouterNodeProviderCacheStore` are **kept** (not deleted as the spec suggested) because ControlPlane still does live quota + integration-key reads for `GetRouterNodeDetails` and key push/revoke. Only the moved model/catalog methods are stripped from the provider. Confirm this is acceptable with the reviewer; if full removal is desired, that is a separate follow-up that also reworks `GetRouterNodeDetails` to read quota from the collector.
- **Ordering/safety:** the collector (Phases 1-2) must be deployed and serving before this phase ships, since ControlPlane routing and WebApp pages now depend on it. The capability replica degrades gracefully (keeps stale hints / empty-store fallback) if the collector is briefly unavailable.
- **Type consistency:** `CapabilityReplicaRefresher`, `CapabilityReplicaOptions`, `NodeModelCapabilityStore.ReplaceAll`, and `CollectorProvider`'s reuse of `NodeModelCatalogResult`/`NodeModelCatalogEntry`/`UnreachableNodeDetail` are consistent across tasks.
