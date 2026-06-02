# Collector Service — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a new `OpenRussiaRouter.CollectorService` (gRPC, port 5005) that periodically polls every enabled router node for its model catalog, caches the result in-memory, and serves it over gRPC — deployed in parallel without changing any existing service behavior.

**Architecture:** A `BackgroundService` poll worker fetches the node list from ControlPlane (`ListRouterNodes` gRPC), polls each enabled node's admin API for models (with `/v1/models` fallback), and atomically swaps an immutable in-memory snapshot. A gRPC service projects that snapshot into three RPCs: `GetCapabilitiesSnapshot` (node→model-ids, for the ControlPlane replica added in Phase 3), `ListNodeModelCatalog` (rich catalog, for WebApp), and `GetNodeModels` (single node). The collector has its own self-contained read-only node-admin HTTP client; ControlPlane's existing collection code is left untouched in this phase (deduplication happens in Phase 3).

**Tech Stack:** .NET 8, ASP.NET Core gRPC (Grpc.AspNetCore), Grpc.Net.ClientFactory, Serilog/Seq, OpenTelemetry/Prometheus, xUnit + FluentAssertions + Moq.

**Spec:** `docs/superpowers/specs/2026-06-01-collector-service-design.md`

---

## File Structure

**New project `src/OpenRussiaRouter.CollectorService/`:**
- `OpenRussiaRouter.CollectorService.csproj` — project file (references Contracts + BuildingBlocks).
- `Program.cs` — host bootstrap (Seq, OTel, gRPC, gRPC client to ControlPlane).
- `Infrastructure/Startup/Startup.cs` — DI + pipeline wiring.
- `Application/NodeAdmin/INodeAdminClient.cs` — read-only node-admin client interface.
- `Application/NodeAdmin/NodeAdminClient.cs` — HTTP implementation (operates on endpoint+token).
- `Application/NodeAdmin/NodeAdminContracts.cs` — JSON DTOs + `NodeAdminException`.
- `Application/Collection/CollectorNode.cs` — DTO describing a node to poll (id, endpoint, token, enabled, status, lastChecked).
- `Application/Collection/NodeCatalogResult.cs` — per-node collection result + `ModelObservation`.
- `Application/Collection/NodeCatalogCollector.cs` — collects one node's catalog (ported logic).
- `Application/Collection/CollectorSnapshot.cs` — immutable snapshot record.
- `Application/Collection/CollectorSnapshotStore.cs` — thread-safe current-snapshot holder.
- `Application/Collection/CatalogProjection.cs` — projects a snapshot into capabilities/catalog/node-models (ported merge logic).
- `Application/Collection/NodeListProvider.cs` — fetches the node list from ControlPlane gRPC.
- `Application/Collection/CollectorOptions.cs` — poll intervals + timeouts.
- `Application/Collection/NodePollingWorker.cs` — `BackgroundService` poll loop.
- `Infrastructure/Grpc/GrpcCollectorService.cs` — gRPC endpoint implementation.
- `appsettings.json` — config.
- `Dockerfile` — container build.

**New contract:**
- `src/OpenRussiaRouter.Contracts/Protos/collector.proto` — collector gRPC contract.

**New test project `tests/OpenRussiaRouter.CollectorService.Tests/`:**
- `OpenRussiaRouter.CollectorService.Tests.csproj`
- `Collection/CollectorSnapshotStoreTests.cs`
- `Collection/CatalogProjectionTests.cs`
- `Collection/NodeCatalogCollectorTests.cs`
- `Collection/NodePollingWorkerTests.cs`
- `Grpc/GrpcCollectorServiceTests.cs`

**Infra (modify):**
- `src/OpenRussiaRouter.Contracts/OpenRussiaRouter.Contracts.csproj` — register `collector.proto`.
- `OpenRussiaRouter.sln` — add the two new projects.
- `docker-compose.yml` — add `collector` service.
- `infra/kustomize/base/services/collector.yaml` (new) + `kustomization.yaml` (modify).

---

## Task 1: Define the collector gRPC contract

> **Important:** The `OpenRussiaRouter.Contracts` project does NOT compile `.proto` files itself — it is a bare project that only *holds* the `.proto` files on disk (plus a BuildingBlocks reference). Each consuming service project (ControlPlane, WebApp, CollectorService, …) links the proto via `<Protobuf Include="..\OpenRussiaRouter.Contracts\Protos\xxx.proto" Link=... GrpcServices=... />` and generates its own copy of the types. **Do not add `<Protobuf>` items or `Grpc.Tools` to the Contracts csproj** — that causes duplicate-type conflicts when a service links the same proto. Creating the `.proto` file is the only change needed here.

**Files:**
- Create: `src/OpenRussiaRouter.Contracts/Protos/collector.proto`

- [ ] **Step 1: Write `collector.proto`**

```proto
syntax = "proto3";

package openrussiarouter.collector.v1;

option csharp_namespace = "OpenRussiaRouter.Contracts.Grpc.Collector.V1";

service Collector {
  // node_id -> supported routable model ids; consumed by ControlPlane's local replica.
  rpc GetCapabilitiesSnapshot (GetCapabilitiesSnapshotRequest) returns (GetCapabilitiesSnapshotResponse);
  // Rich merged catalog across all nodes; consumed by WebApp.
  rpc ListNodeModelCatalog (ListNodeModelCatalogRequest) returns (ListNodeModelCatalogResponse);
  // Routable model ids for a single node.
  rpc GetNodeModels (GetNodeModelsRequest) returns (GetNodeModelsResponse);
}

message GetCapabilitiesSnapshotRequest {}

message NodeCapability {
  string node_id = 1;
  repeated string model_ids = 2;
}

message GetCapabilitiesSnapshotResponse {
  repeated NodeCapability nodes = 1;
  // ISO-8601 round-trip ("O") UTC timestamp of the snapshot; empty if nothing collected yet.
  string collected_at_utc = 2;
}

message ListNodeModelCatalogRequest {}

message UpstreamModelEntry {
  string upstream_id = 1;
  string public_id = 2;
  string display_name = 3;
  string model_type = 4;
  repeated string source_providers = 5;
  repeated string node_ids = 6;
}

message UnreachableNodeInfo {
  string node_id = 1;
  string request = 2;
  string response = 3;
}

message ListNodeModelCatalogResponse {
  repeated UpstreamModelEntry entries = 1;
  repeated string unreachable_node_ids = 2;
  repeated UnreachableNodeInfo unreachable_nodes = 3;
  string collected_at_utc = 4;
}

message GetNodeModelsRequest {
  string node_id = 1;
}

message GetNodeModelsResponse {
  repeated string public_ids = 1;
  string error = 2;
}
```

- [ ] **Step 2: Verify the file is well-formed (no csproj change)**

No csproj edit is required — Contracts only stores the `.proto`. The contract is exercised when a service links it (Task 2 onward). To sanity-check the proto syntax now, you may temporarily build any project that already links protos, or simply proceed; the first real codegen happens in Task 2's CollectorService csproj.

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.Contracts/Protos/collector.proto
git commit -m "feat(contracts): add collector.proto gRPC contract"
```

---

## Task 2: Scaffold the CollectorService project

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj`
- Create: `src/OpenRussiaRouter.CollectorService/appsettings.json`
- Modify: `OpenRussiaRouter.sln`

- [ ] **Step 1: Create the csproj**

`src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj` (mirrors ControlPlane's csproj; the collector is a gRPC server and a ControlPlane gRPC client, no DB):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <Protobuf Include="..\OpenRussiaRouter.Contracts\Protos\collector.proto" GrpcServices="Server" Link="Protos\collector.proto" />
    <Protobuf Include="..\OpenRussiaRouter.Contracts\Protos\control-plane.proto" GrpcServices="Client" Link="Protos\control-plane.proto" />
    <ProjectReference Include="..\OpenRussiaRouter.BuildingBlocks\OpenRussiaRouter.BuildingBlocks.csproj" />
    <ProjectReference Include="..\OpenRussiaRouter.Contracts\OpenRussiaRouter.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Google.Protobuf"  />
    <PackageReference Include="Grpc.AspNetCore"  />
    <PackageReference Include="Grpc.Net.ClientFactory"  />
    <PackageReference Include="Grpc.Tools" >
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

</Project>
```

> Note: this project links `control-plane.proto` as `GrpcServices="Client"` only — the collector consumes ControlPlane but does not serve it. Package versions are centrally managed by `Directory.Packages.props`, so no `Version=` attributes.

- [ ] **Step 2: Create appsettings.json**

`src/OpenRussiaRouter.CollectorService/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Kestrel": {
    "Endpoints": {
      "Grpc": {
        "Url": "http://localhost:5005",
        "Protocols": "Http2"
      }
    }
  },
  "GrpcServices": {
    "ControlPlane": "http://localhost:5003",
    "CallDeadlineSeconds": 30
  },
  "Collector": {
    "NodePollInterval": "00:01:00",
    "NodeListRefreshInterval": "00:01:00",
    "PerNodeTimeout": "00:00:10"
  }
}
```

- [ ] **Step 3: Add both projects to the solution**

Run:
```bash
dotnet sln OpenRussiaRouter.sln add src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj
```
Expected: "Project ... added to the solution." (The test project is added in Task 9.)

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj src/OpenRussiaRouter.CollectorService/appsettings.json OpenRussiaRouter.sln
git commit -m "feat(collector): scaffold CollectorService project"
```

---

## Task 3: Node-admin read-only HTTP client

The collector needs its own read-only client (operating on `endpoint + controlToken`, not the ControlPlane `RouterNode` domain entity). The DTOs are copied from `RouterNodeProviderContracts.cs` (read-only subset) — intentional thin duplication kept until Phase 3.

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminContracts.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/INodeAdminClient.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminClient.cs`
- Test: `tests/OpenRussiaRouter.CollectorService.Tests/NodeAdmin/NodeAdminClientTests.cs` (added in Task 9 once the test project exists; for now implement the client and rely on Task 9 tests)

- [ ] **Step 1: Create the DTOs**

`src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminContracts.cs`:

```csharp
using System.Net;

namespace OpenRussiaRouter.CollectorService.Application.NodeAdmin;

public sealed record NodeProvidersWithModelsResponse
{
    public IReadOnlyList<NodeProviderWithModels> Connections { get; init; } = [];
}

public sealed record NodeProviderWithModels
{
    public string Id { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string AuthType { get; init; } = string.Empty;
    public string? Name { get; init; }
    public bool IsActive { get; init; }
    public string? TestStatus { get; init; }
    public IReadOnlyList<NodeProviderModelInfo> Models { get; init; } = [];
}

public sealed record NodeProviderModelInfo
{
    public string Id { get; init; } = string.Empty;
    public string PublicId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
}

public sealed record NodeModelsResponse
{
    public IReadOnlyList<NodeModelInfo> Data { get; init; } = [];
}

public sealed record NodeModelInfo
{
    public string Id { get; init; } = string.Empty;
}

public sealed class NodeAdminException : Exception
{
    public NodeAdminException(
        HttpStatusCode statusCode,
        string message,
        string? requestMethod = null,
        string? requestUri = null,
        string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        RequestMethod = requestMethod;
        RequestUri = requestUri;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? RequestMethod { get; }
    public string? RequestUri { get; }
    public string? ResponseBody { get; }
}
```

- [ ] **Step 2: Create the interface**

`src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/INodeAdminClient.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.NodeAdmin;

public interface INodeAdminClient
{
    string DescribeListProvidersRequest(Uri endpoint);
    string DescribeListModelsRequest(Uri endpoint);

    Task<NodeProvidersWithModelsResponse> ListProvidersWithModelsAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken);

    Task<NodeModelsResponse> ListModelsAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Implement the client**

`src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminClient.cs` (read-only adaptation of `RouterNodeProvider.cs`; same headers and error shape):

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenRussiaRouter.CollectorService.Application.NodeAdmin;

public sealed class NodeAdminClient(HttpClient httpClient) : INodeAdminClient
{
    private const string ModelsPath = "/v1/models";
    private const string ProvidersWithModelsPath = "/api/integrations/providers/with-models";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string DescribeListProvidersRequest(Uri endpoint) =>
        $"GET {new Uri(endpoint, ProvidersWithModelsPath)}";

    public string DescribeListModelsRequest(Uri endpoint) =>
        $"GET {new Uri(endpoint, ModelsPath)}";

    public Task<NodeProvidersWithModelsResponse> ListProvidersWithModelsAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken) =>
        SendAsync<NodeProvidersWithModelsResponse>(endpoint, controlToken, ProvidersWithModelsPath, cancellationToken);

    public Task<NodeModelsResponse> ListModelsAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken) =>
        SendAsync<NodeModelsResponse>(endpoint, controlToken, ModelsPath, cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(
        Uri endpoint, string controlToken, string relativePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (string.IsNullOrWhiteSpace(controlToken))
            throw new InvalidOperationException($"Control token is not set for endpoint '{endpoint}'.");

        var absoluteUri = new Uri(endpoint, relativePath);
        using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + controlToken);
        request.Headers.TryAddWithoutValidation("X-ORR-Node-Token", controlToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new NodeAdminException(
                response.StatusCode,
                $"Node admin request failed with HTTP {(int)response.StatusCode}.",
                requestMethod: "GET",
                requestUri: absoluteUri.ToString(),
                responseBody: body);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return value ?? throw new InvalidOperationException("Node admin response body was empty.");
    }
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj -nr:false -m:1`
Expected: Build succeeded (Program.cs is added in Task 8; until then the `OutputType=Exe` build will warn about missing entry point — that's fine, library compilation of the classes succeeds. If it errors on missing Main, add an empty `Program.cs` containing `// placeholder` and a `return;` top-level statement, to be replaced in Task 8).

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/
git commit -m "feat(collector): add read-only node-admin HTTP client"
```

---

## Task 4: Snapshot model + store

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorNode.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogResult.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorSnapshot.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorSnapshotStore.cs`
- Test: `tests/.../Collection/CollectorSnapshotStoreTests.cs` (in Task 9)

- [ ] **Step 1: Create the node + result + observation types**

`src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorNode.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.Collection;

/// <summary>A node to poll, as projected from ControlPlane's node registry.</summary>
public sealed record CollectorNode(
    string Id,
    Uri EndpointUrl,
    string ControlToken,
    bool Enabled,
    string Status,
    bool HasBeenHealthChecked);
```

`src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogResult.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.Collection;

public sealed record NodeCatalogResult(
    string NodeId,
    bool Unreachable,
    IReadOnlyList<ModelObservation> Observations,
    string? Request,
    string? Response);

public sealed class ModelObservation
{
    public string UpstreamId { get; init; } = string.Empty;
    public string PublicId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ModelType { get; init; } = string.Empty;
    public IReadOnlyList<string> SourceProviders { get; init; } = [];
    public int Rank { get; init; }
}
```

`src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorSnapshot.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.Collection;

public sealed record CollectorSnapshot(
    IReadOnlyList<NodeCatalogResult> Nodes,
    DateTimeOffset? CollectedAtUtc)
{
    public static readonly CollectorSnapshot Empty = new([], null);
}
```

- [ ] **Step 2: Write the failing test for the store**

Add to `tests/.../Collection/CollectorSnapshotStoreTests.cs` (test project created in Task 9; if executing strictly in order, defer running this test until Task 9 and treat Steps 2-4 here as "write store + its test together"):

```csharp
using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Collection;

namespace OpenRussiaRouter.CollectorService.Tests.Collection;

public class CollectorSnapshotStoreTests
{
    [Fact]
    public void Current_BeforeAnySet_ReturnsEmpty()
    {
        var store = new CollectorSnapshotStore();
        store.Current.Should().BeSameAs(CollectorSnapshot.Empty);
    }

    [Fact]
    public void Set_ThenCurrent_ReturnsLatestSnapshot()
    {
        var store = new CollectorSnapshotStore();
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("n1", false, [], null, null)],
            DateTimeOffset.UnixEpoch);

        store.Set(snapshot);

        store.Current.Should().BeSameAs(snapshot);
    }
}
```

- [ ] **Step 3: Implement the store**

`src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorSnapshotStore.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.Collection;

/// <summary>
/// Holds the most recently collected snapshot. Reads are lock-free; writes swap the
/// reference atomically so consumers always see a fully-built, consistent snapshot.
/// </summary>
public sealed class CollectorSnapshotStore
{
    private volatile CollectorSnapshot _current = CollectorSnapshot.Empty;

    public CollectorSnapshot Current => _current;

    public void Set(CollectorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = snapshot;
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorNode.cs src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogResult.cs src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorSnapshot.cs src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorSnapshotStore.cs
git commit -m "feat(collector): add in-memory snapshot model and store"
```

---

## Task 5: Catalog projection (ported merge logic)

Port the projection logic from `GrpcRouterControlPlaneService.cs` (lines ~395-452 for the accumulator merge, ~593-610 for `ChooseField`/`GetRoutableModelId`, plus the `CatalogAccumulator` class ~629-640) into a standalone static helper.

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Application/Collection/CatalogProjection.cs`
- Test: `tests/.../Collection/CatalogProjectionTests.cs` (in Task 9)

- [ ] **Step 1: Implement the projection helper**

`src/OpenRussiaRouter.CollectorService/Application/Collection/CatalogProjection.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.Collection;

/// <summary>Pure projections over a collected snapshot. No I/O.</summary>
public static class CatalogProjection
{
    /// <summary>node_id -> ordered set of routable model ids (public id, else upstream id).</summary>
    public static IReadOnlyList<(string NodeId, IReadOnlyList<string> ModelIds)> Capabilities(
        CollectorSnapshot snapshot)
    {
        var result = new List<(string, IReadOnlyList<string>)>();
        foreach (var node in snapshot.Nodes.Where(n => !n.Unreachable))
        {
            var modelIds = node.Observations
                .Select(GetRoutableModelId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            result.Add((node.NodeId, modelIds));
        }
        return result;
    }

    /// <summary>Routable model ids for a single node, or null if the node is absent/unreachable.</summary>
    public static IReadOnlyList<string>? NodeModels(CollectorSnapshot snapshot, string nodeId)
    {
        var node = snapshot.Nodes.FirstOrDefault(n => string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));
        if (node is null || node.Unreachable)
            return null;

        return node.Observations
            .Select(GetRoutableModelId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Merged rich catalog across all reachable nodes.</summary>
    public static IReadOnlyList<CatalogEntry> Catalog(CollectorSnapshot snapshot)
    {
        var accumulators = new Dictionary<string, CatalogAccumulator>(StringComparer.Ordinal);
        foreach (var result in snapshot.Nodes.Where(n => !n.Unreachable))
        {
            foreach (var observation in result.Observations)
            {
                if (!accumulators.TryGetValue(observation.UpstreamId, out var acc))
                {
                    acc = new CatalogAccumulator { UpstreamId = observation.UpstreamId };
                    accumulators[observation.UpstreamId] = acc;
                }

                (acc.PublicId, acc.PublicIdRank) =
                    ChooseField(acc.PublicId, acc.PublicIdRank, observation.PublicId, observation.Rank);
                (acc.DisplayName, acc.DisplayNameRank) =
                    ChooseField(acc.DisplayName, acc.DisplayNameRank, observation.DisplayName, observation.Rank);
                (acc.ModelType, acc.ModelTypeRank) =
                    ChooseField(acc.ModelType, acc.ModelTypeRank, observation.ModelType, observation.Rank);

                foreach (var source in observation.SourceProviders)
                    if (!string.IsNullOrWhiteSpace(source))
                        acc.SourceProviders.Add(source);

                acc.NodeIds.Add(result.NodeId);
            }
        }

        return accumulators.Values
            .OrderBy(a => a.UpstreamId, StringComparer.Ordinal)
            .Select(acc => new CatalogEntry(
                acc.UpstreamId,
                string.IsNullOrWhiteSpace(acc.PublicId) ? acc.UpstreamId : acc.PublicId,
                acc.DisplayName,
                acc.ModelType,
                acc.SourceProviders.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                acc.NodeIds.OrderBy(s => s, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    private static string GetRoutableModelId(ModelObservation o) =>
        string.IsNullOrWhiteSpace(o.PublicId) ? o.UpstreamId : o.PublicId;

    private static (string Value, int Rank) ChooseField(string current, int currentRank, string incoming, int incomingRank)
    {
        if (string.IsNullOrWhiteSpace(incoming))
            return (current, currentRank);
        if (incomingRank > currentRank || (incomingRank == currentRank && string.IsNullOrWhiteSpace(current)))
            return (incoming, incomingRank);
        return (current, currentRank);
    }

    private sealed class CatalogAccumulator
    {
        public string UpstreamId { get; init; } = string.Empty;
        public string PublicId { get; set; } = string.Empty;
        public int PublicIdRank { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int DisplayNameRank { get; set; }
        public string ModelType { get; set; } = string.Empty;
        public int ModelTypeRank { get; set; }
        public HashSet<string> SourceProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record CatalogEntry(
    string UpstreamId,
    string PublicId,
    string DisplayName,
    string ModelType,
    IReadOnlyList<string> SourceProviders,
    IReadOnlyList<string> NodeIds);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj -nr:false -m:1`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Collection/CatalogProjection.cs
git commit -m "feat(collector): add catalog projection over snapshot"
```

---

## Task 6: Per-node catalog collector + options

Port `CollectNodeCatalogAsync` / `CollectFallbackModelsAsync` / `IsTransientProviderError` / `FormatRequest` / `FormatResponse` / `IsRoutableForCatalog` from `GrpcRouterControlPlaneService.cs:454-591,546-552` into a dedicated collector class operating on `CollectorNode` + `INodeAdminClient`.

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorOptions.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogCollector.cs`
- Test: `tests/.../Collection/NodeCatalogCollectorTests.cs` (in Task 9)

- [ ] **Step 1: Create options**

`src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.CollectorService.Application.Collection;

public sealed class CollectorOptions
{
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan NodePollInterval { get; set; } = TimeSpan.FromMinutes(1);

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan NodeListRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan PerNodeTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
```

- [ ] **Step 2: Implement the per-node collector**

`src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogCollector.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;

namespace OpenRussiaRouter.CollectorService.Application.Collection;

/// <summary>Collects a single node's model catalog with a per-node timeout and /v1/models fallback.</summary>
public sealed class NodeCatalogCollector(
    INodeAdminClient client,
    IOptionsMonitor<CollectorOptions> options,
    ILogger<NodeCatalogCollector> logger)
{
    public async Task<NodeCatalogResult> CollectAsync(CollectorNode node, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.CurrentValue.PerNodeTimeout);
        var nodeToken = timeoutCts.Token;

        NodeProvidersWithModelsResponse providersWithModels;
        try
        {
            providersWithModels = await client
                .ListProvidersWithModelsAsync(node.EndpointUrl, node.ControlToken, nodeToken)
                .ConfigureAwait(false);
        }
        catch (NodeAdminException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await CollectFallbackModelsAsync(node, nodeToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            logger.LogWarning(ex, "ListProvidersWithModels failed for node {NodeId}", node.Id);
            return new NodeCatalogResult(
                node.Id, Unreachable: true, [],
                FormatRequest(ex, client.DescribeListProvidersRequest(node.EndpointUrl)),
                FormatResponse(ex));
        }

        var observations = new List<ModelObservation>();
        foreach (var connection in providersWithModels.Connections)
        {
            if (!connection.IsActive || string.IsNullOrWhiteSpace(connection.Id))
                continue;
            foreach (var model in connection.Models)
            {
                if (string.IsNullOrWhiteSpace(model.Id) || !model.Enabled)
                    continue;
                observations.Add(new ModelObservation
                {
                    UpstreamId = model.Id,
                    PublicId = string.IsNullOrWhiteSpace(model.PublicId) ? string.Empty : model.PublicId,
                    DisplayName = model.Name ?? string.Empty,
                    ModelType = model.Type ?? string.Empty,
                    SourceProviders = model.Sources ?? [],
                    Rank = 2
                });
            }
        }

        return new NodeCatalogResult(node.Id, Unreachable: false, observations, null, null);
    }

    private async Task<NodeCatalogResult> CollectFallbackModelsAsync(
        CollectorNode node, CancellationToken nodeToken, CancellationToken cancellationToken)
    {
        try
        {
            var models = await client.ListModelsAsync(node.EndpointUrl, node.ControlToken, nodeToken).ConfigureAwait(false);
            var observations = models.Data
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => new ModelObservation
                {
                    UpstreamId = m.Id,
                    PublicId = m.Id,
                    Rank = 1
                })
                .ToList();
            return new NodeCatalogResult(node.Id, Unreachable: false, observations, null, null);
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Fallback model listing failed for node {NodeId}", node.Id);
            return new NodeCatalogResult(
                node.Id, Unreachable: true, [],
                FormatRequest(ex, client.DescribeListModelsRequest(node.EndpointUrl)),
                FormatResponse(ex));
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken) =>
        ex is NodeAdminException or InvalidOperationException or HttpRequestException ||
        (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static string FormatRequest(Exception ex, string fallbackRequest)
    {
        if (ex is NodeAdminException p && !string.IsNullOrWhiteSpace(p.RequestMethod) && !string.IsNullOrWhiteSpace(p.RequestUri))
            return $"{p.RequestMethod} {p.RequestUri}";
        return fallbackRequest;
    }

    private static string FormatResponse(Exception ex)
    {
        if (ex is NodeAdminException p)
        {
            var body = p.ResponseBody;
            const int maxBody = 1024;
            if (!string.IsNullOrEmpty(body) && body.Length > maxBody)
                body = body[..maxBody] + "… (truncated)";
            var statusLine = $"HTTP {(int)p.StatusCode} {p.StatusCode}";
            return string.IsNullOrWhiteSpace(body) ? statusLine : $"{statusLine}\n{body}";
        }
        if (ex is TaskCanceledException tce)
            return tce.InnerException is TimeoutException
                ? "TimeoutException: request timed out"
                : $"TaskCanceledException: {tce.Message}";
        if (ex is HttpRequestException hre)
            return $"HttpRequestException: {hre.Message}";
        if (ex is InvalidOperationException ioe)
            return $"InvalidOperationException: {ioe.Message}";
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    /// <summary>Enabled AND (never health-checked OR last check was not Unhealthy).</summary>
    public static bool IsRoutableForCatalog(CollectorNode node) =>
        node.Enabled &&
        (!node.HasBeenHealthChecked || !string.Equals(node.Status, "Unhealthy", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj -nr:false -m:1`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Collection/CollectorOptions.cs src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogCollector.cs
git commit -m "feat(collector): add per-node catalog collector with fallback"
```

---

## Task 7: Node-list provider + polling worker

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Application/Collection/NodeListProvider.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/Collection/NodePollingWorker.cs`
- Test: `tests/.../Collection/NodePollingWorkerTests.cs` (in Task 9)

- [ ] **Step 1: Implement the node-list provider**

`src/OpenRussiaRouter.CollectorService/Application/Collection/NodeListProvider.cs` — wraps the ControlPlane gRPC client and maps `RouterNodeSummary` → `CollectorNode`:

```csharp
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;

namespace OpenRussiaRouter.CollectorService.Application.Collection;

public interface INodeListProvider
{
    Task<IReadOnlyList<CollectorNode>> GetNodesAsync(CancellationToken cancellationToken);
}

public sealed class NodeListProvider(RouterControlPlane.RouterControlPlaneClient client) : INodeListProvider
{
    public async Task<IReadOnlyList<CollectorNode>> GetNodesAsync(CancellationToken cancellationToken)
    {
        var response = await client.ListRouterNodesAsync(
            new ListRouterNodesRequest(), cancellationToken: cancellationToken);

        var nodes = new List<CollectorNode>(response.Nodes.Count);
        foreach (var n in response.Nodes)
        {
            if (!Uri.TryCreate(n.EndpointUrl, UriKind.Absolute, out var endpoint))
                continue;
            nodes.Add(new CollectorNode(
                Id: n.NodeId,
                EndpointUrl: endpoint,
                ControlToken: n.ControlToken,
                // ControlPlane maps a disabled node's status to "Disabled"; treat anything not
                // "Disabled" as enabled for polling purposes.
                Enabled: !string.Equals(n.Status, "Disabled", StringComparison.OrdinalIgnoreCase),
                Status: n.Status,
                HasBeenHealthChecked: !string.IsNullOrWhiteSpace(n.LastCheckedAtUtc)));
        }
        return nodes;
    }
}
```

- [ ] **Step 2: Implement the polling worker**

`src/OpenRussiaRouter.CollectorService/Application/Collection/NodePollingWorker.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.CollectorService.Application.Collection;

public sealed class NodePollingWorker(
    INodeListProvider nodeListProvider,
    NodeCatalogCollector collector,
    CollectorSnapshotStore store,
    IOptionsMonitor<CollectorOptions> options,
    ILogger<NodePollingWorker> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    /// <summary>One poll cycle: fetch node list, collect each routable node, swap snapshot.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CollectorNode> nodes;
        try
        {
            nodes = await nodeListProvider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the previous snapshot if ControlPlane is unreachable.
            logger.LogWarning(ex, "Failed to fetch node list from ControlPlane; keeping previous snapshot");
            return;
        }

        var routable = nodes.Where(NodeCatalogCollector.IsRoutableForCatalog).ToList();
        var results = await Task.WhenAll(
            routable.Select(node => collector.CollectAsync(node, cancellationToken))).ConfigureAwait(false);

        store.Set(new CollectorSnapshot(results, timeProvider.GetUtcNow()));
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
                logger.LogError(ex, "Node polling cycle failed");
            }

            try
            {
                await Task.Delay(options.CurrentValue.NodePollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
```

> Note: `NodeListRefreshInterval` is reserved for a future independent node-list refresh cadence; in Phase 1 the node list is fetched at the start of every poll cycle (simpler, and the poll interval is the same default). It is kept in options so Phase 2/3 can decouple the cadences without a config migration.

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj -nr:false -m:1`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Collection/NodeListProvider.cs src/OpenRussiaRouter.CollectorService/Application/Collection/NodePollingWorker.cs
git commit -m "feat(collector): add node-list provider and polling worker"
```

---

## Task 8: gRPC service + host bootstrap

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Infrastructure/Grpc/GrpcCollectorService.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Infrastructure/Startup/Startup.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Program.cs`
- Test: `tests/.../Grpc/GrpcCollectorServiceTests.cs` (in Task 9)

- [ ] **Step 1: Implement the gRPC service**

`src/OpenRussiaRouter.CollectorService/Infrastructure/Grpc/GrpcCollectorService.cs`:

```csharp
using Grpc.Core;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.CollectorService.Infrastructure.Grpc;

public sealed class GrpcCollectorService(CollectorSnapshotStore store) : Collector.CollectorBase
{
    public override Task<GetCapabilitiesSnapshotResponse> GetCapabilitiesSnapshot(
        GetCapabilitiesSnapshotRequest request, ServerCallContext context)
    {
        var snapshot = store.Current;
        var response = new GetCapabilitiesSnapshotResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty
        };
        foreach (var (nodeId, modelIds) in CatalogProjection.Capabilities(snapshot))
        {
            var cap = new NodeCapability { NodeId = nodeId };
            cap.ModelIds.AddRange(modelIds);
            response.Nodes.Add(cap);
        }
        return Task.FromResult(response);
    }

    public override Task<ListNodeModelCatalogResponse> ListNodeModelCatalog(
        ListNodeModelCatalogRequest request, ServerCallContext context)
    {
        var snapshot = store.Current;
        var response = new ListNodeModelCatalogResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty
        };

        foreach (var entry in CatalogProjection.Catalog(snapshot))
        {
            var e = new UpstreamModelEntry
            {
                UpstreamId = entry.UpstreamId,
                PublicId = entry.PublicId,
                DisplayName = entry.DisplayName,
                ModelType = entry.ModelType
            };
            e.SourceProviders.AddRange(entry.SourceProviders);
            e.NodeIds.AddRange(entry.NodeIds);
            response.Entries.Add(e);
        }

        foreach (var node in snapshot.Nodes.Where(n => n.Unreachable).OrderBy(n => n.NodeId, StringComparer.Ordinal))
        {
            response.UnreachableNodeIds.Add(node.NodeId);
            response.UnreachableNodes.Add(new UnreachableNodeInfo
            {
                NodeId = node.NodeId,
                Request = node.Request ?? string.Empty,
                Response = node.Response ?? string.Empty
            });
        }

        return Task.FromResult(response);
    }

    public override Task<GetNodeModelsResponse> GetNodeModels(
        GetNodeModelsRequest request, ServerCallContext context)
    {
        var snapshot = store.Current;
        var models = CatalogProjection.NodeModels(snapshot, request.NodeId);
        if (models is null)
            return Task.FromResult(new GetNodeModelsResponse { Error = "Node not found or unreachable" });

        var response = new GetNodeModelsResponse();
        response.PublicIds.AddRange(models);
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 2: Implement Startup (DI + pipeline)**

`src/OpenRussiaRouter.CollectorService/Infrastructure/Startup/Startup.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BuildingBlocks.Grpc;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;
using OpenRussiaRouter.CollectorService.Infrastructure.Grpc;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;

namespace OpenRussiaRouter.CollectorService.Infrastructure.Startup;

public static class Startup
{
    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc();
        services.AddHealthChecks();

        services.AddOptions<CollectorOptions>()
            .Bind(configuration.GetSection("Collector"))
            .ValidateDataAnnotations();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CollectorSnapshotStore>();
        services.AddSingleton<NodeCatalogCollector>();
        services.AddSingleton<INodeListProvider, NodeListProvider>();
        services.AddHttpClient<INodeAdminClient, NodeAdminClient>(c => c.Timeout = TimeSpan.FromSeconds(10));

        var grpcCallDeadline = TimeSpan.FromSeconds(
            configuration.GetValue("GrpcServices:CallDeadlineSeconds", 30));
        var controlPlaneUrl = configuration["GrpcServices:ControlPlane"] ?? "http://localhost:5003";
        services.AddGrpcClient<RouterControlPlane.RouterControlPlaneClient>(o => o.Address = new Uri(controlPlaneUrl))
            .ConfigureChannel(o =>
            {
                o.HttpHandler = new SocketsHttpHandler
                {
                    PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                };
            })
            .AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));

        services.AddHostedService<NodePollingWorker>();

        return services;
    }

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.UseLoggedExceptionHandler("/error");
        app.MapGrpcService<GrpcCollectorService>();
        app.MapHealthChecks("/health");
        return app;
    }
}
```

> Verify the `GrpcDeadlineInterceptor` namespace is `OpenRussiaRouter.BuildingBlocks.Grpc` by checking `src/OpenRussiaRouter.BuildingBlocks/Grpc/GrpcDeadlineInterceptor.cs`; adjust the `using` if it differs. Verify `UseLoggedExceptionHandler` exists in `OpenRussiaRouter.BuildingBlocks.Logging` (it is used by ControlPlane's `Startup.cs`).

- [ ] **Step 3: Implement Program.cs**

`src/OpenRussiaRouter.CollectorService/Program.cs` (mirrors ControlPlane's `Program.cs`, minus migrations):

```csharp
using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.CollectorService.Infrastructure.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("collector");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.Collector", "OpenRussiaRouter");

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();

public partial class Program;
```

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1` (run `dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1` first if restore is stale)
Expected: Build succeeded with the new CollectorService project compiling.

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Infrastructure/ src/OpenRussiaRouter.CollectorService/Program.cs
git commit -m "feat(collector): add gRPC service and host bootstrap"
```

---

## Task 9: Test project + unit tests

**Files:**
- Create: `tests/OpenRussiaRouter.CollectorService.Tests/OpenRussiaRouter.CollectorService.Tests.csproj`
- Create test files listed below.
- Modify: `OpenRussiaRouter.sln`

- [ ] **Step 1: Create the test csproj**

`tests/OpenRussiaRouter.CollectorService.Tests/OpenRussiaRouter.CollectorService.Tests.csproj` (mirrors the ControlPlane test csproj, minus DB packages):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector"  />
    <PackageReference Include="FluentAssertions"  />
    <PackageReference Include="Grpc.Net.Client"  />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing"  />
    <PackageReference Include="Microsoft.NET.Test.Sdk"  />
    <PackageReference Include="Moq"  />
    <PackageReference Include="xunit"  />
    <PackageReference Include="xunit.runner.visualstudio"  />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenRussiaRouter.CollectorService\OpenRussiaRouter.CollectorService.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add the test project to the solution**

Run: `dotnet sln OpenRussiaRouter.sln add tests/OpenRussiaRouter.CollectorService.Tests/OpenRussiaRouter.CollectorService.Tests.csproj`
Expected: "Project ... added to the solution."

- [ ] **Step 3: Add `CollectorSnapshotStoreTests.cs`**

Use the test code already shown in Task 4 Step 2, at `tests/OpenRussiaRouter.CollectorService.Tests/Collection/CollectorSnapshotStoreTests.cs`.

- [ ] **Step 4: Add `CatalogProjectionTests.cs`**

`tests/OpenRussiaRouter.CollectorService.Tests/Collection/CatalogProjectionTests.cs`:

```csharp
using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Collection;

namespace OpenRussiaRouter.CollectorService.Tests.Collection;

public class CatalogProjectionTests
{
    private static ModelObservation Obs(string upstream, string publicId, int rank = 2, string type = "", params string[] sources) =>
        new() { UpstreamId = upstream, PublicId = publicId, ModelType = type, Rank = rank, SourceProviders = sources };

    [Fact]
    public void Capabilities_UsesPublicIdWhenPresent_ElseUpstream_OrderedAndDistinct()
    {
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("n1", false,
                [Obs("up-b", "pub-b"), Obs("up-a", ""), Obs("up-b", "pub-b")], null, null)],
            DateTimeOffset.UnixEpoch);

        var caps = CatalogProjection.Capabilities(snapshot);

        caps.Should().ContainSingle();
        caps[0].NodeId.Should().Be("n1");
        caps[0].ModelIds.Should().Equal("pub-b", "up-a");
    }

    [Fact]
    public void Capabilities_SkipsUnreachableNodes()
    {
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("dead", true, [], "req", "resp")],
            DateTimeOffset.UnixEpoch);

        CatalogProjection.Capabilities(snapshot).Should().BeEmpty();
    }

    [Fact]
    public void Catalog_RichRank2_OverridesFallbackRank1_AndMergesNodeIds()
    {
        var snapshot = new CollectorSnapshot(
        [
            new NodeCatalogResult("n1", false, [Obs("gpt", "gpt", rank: 1)], null, null),
            new NodeCatalogResult("n2", false, [Obs("gpt", "gpt-public", rank: 2, type: "text", "openai")], null, null),
        ], DateTimeOffset.UnixEpoch);

        var catalog = CatalogProjection.Catalog(snapshot);

        catalog.Should().ContainSingle();
        var entry = catalog[0];
        entry.UpstreamId.Should().Be("gpt");
        entry.PublicId.Should().Be("gpt-public");
        entry.ModelType.Should().Be("text");
        entry.SourceProviders.Should().Equal("openai");
        entry.NodeIds.Should().Equal("n1", "n2");
    }

    [Fact]
    public void NodeModels_ReturnsNullForUnknownNode()
    {
        var snapshot = new CollectorSnapshot([], DateTimeOffset.UnixEpoch);
        CatalogProjection.NodeModels(snapshot, "missing").Should().BeNull();
    }
}
```

- [ ] **Step 5: Add `NodeCatalogCollectorTests.cs`**

`tests/OpenRussiaRouter.CollectorService.Tests/Collection/NodeCatalogCollectorTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;

namespace OpenRussiaRouter.CollectorService.Tests.Collection;

public class NodeCatalogCollectorTests
{
    private static CollectorNode Node() =>
        new("n1", new Uri("http://node/"), "tok", Enabled: true, Status: "Healthy", HasBeenHealthChecked: true);

    private static NodeCatalogCollector Build(INodeAdminClient client) =>
        new(client, Options.Create(new CollectorOptions()).ToMonitor(), NullLogger<NodeCatalogCollector>.Instance);

    [Fact]
    public async Task CollectAsync_MapsActiveEnabledModels_AsRank2()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections =
                [
                    new NodeProviderWithModels
                    {
                        Id = "c1", IsActive = true,
                        Models = [new NodeProviderModelInfo { Id = "gpt", PublicId = "gpt-pub", Enabled = true, Type = "text" }]
                    }
                ]
            });

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Unreachable.Should().BeFalse();
        result.Observations.Should().ContainSingle();
        result.Observations[0].PublicId.Should().Be("gpt-pub");
        result.Observations[0].Rank.Should().Be(2);
    }

    [Fact]
    public async Task CollectAsync_FallsBackToV1Models_On404()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NodeAdminException(HttpStatusCode.NotFound, "404"));
        client.Setup(c => c.ListModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeModelsResponse { Data = [new NodeModelInfo { Id = "fallback" }] });
        client.Setup(c => c.DescribeListModelsRequest(It.IsAny<Uri>())).Returns("GET http://node/v1/models");

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Unreachable.Should().BeFalse();
        result.Observations.Should().ContainSingle(o => o.UpstreamId == "fallback" && o.Rank == 1);
    }

    [Fact]
    public async Task CollectAsync_MarksUnreachable_OnTransientError()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NodeAdminException(HttpStatusCode.InternalServerError, "boom"));
        client.Setup(c => c.DescribeListProvidersRequest(It.IsAny<Uri>())).Returns("GET http://node/api/integrations/providers/with-models");

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Unreachable.Should().BeTrue();
        result.Response.Should().Contain("HTTP 500");
    }

    [Theory]
    [InlineData(true, "Healthy", true, true)]
    [InlineData(true, "Unhealthy", true, false)]
    [InlineData(true, "Unknown", false, true)]
    [InlineData(false, "Healthy", true, false)]
    public void IsRoutableForCatalog(bool enabled, string status, bool checkedBefore, bool expected)
    {
        var node = new CollectorNode("n", new Uri("http://n/"), "t", enabled, status, checkedBefore);
        NodeCatalogCollector.IsRoutableForCatalog(node).Should().Be(expected);
    }
}

internal static class OptionsTestExtensions
{
    public static IOptionsMonitor<T> ToMonitor<T>(this IOptions<T> options) => new StaticMonitor<T>(options.Value);

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
```

- [ ] **Step 6: Add `NodePollingWorkerTests.cs`**

`tests/OpenRussiaRouter.CollectorService.Tests/Collection/NodePollingWorkerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;

namespace OpenRussiaRouter.CollectorService.Tests.Collection;

public class NodePollingWorkerTests
{
    private static NodeCatalogCollector Collector(INodeAdminClient client) =>
        new(client, Options.Create(new CollectorOptions()).ToMonitor(), NullLogger<NodeCatalogCollector>.Instance);

    [Fact]
    public async Task RunOnceAsync_PollsRoutableNodes_AndSwapsSnapshot()
    {
        var nodes = new Mock<INodeListProvider>();
        nodes.Setup(n => n.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectorNode>
            {
                new("n1", new Uri("http://n1/"), "t", true, "Healthy", true)
            });

        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.ListProvidersWithModelsAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProvidersWithModelsResponse
            {
                Connections = [new NodeProviderWithModels { Id = "c1", IsActive = true, Models = [new NodeProviderModelInfo { Id = "m", PublicId = "m", Enabled = true }] }]
            });

        var store = new CollectorSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), store,
            Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);

        await worker.RunOnceAsync(CancellationToken.None);

        store.Current.Nodes.Should().ContainSingle(n => n.NodeId == "n1");
        store.Current.CollectedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnceAsync_KeepsPreviousSnapshot_WhenNodeListFails()
    {
        var nodes = new Mock<INodeListProvider>();
        nodes.Setup(n => n.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("control plane down"));

        var store = new CollectorSnapshotStore();
        var previous = new CollectorSnapshot([new NodeCatalogResult("old", false, [], null, null)], DateTimeOffset.UnixEpoch);
        store.Set(previous);

        var client = new Mock<INodeAdminClient>();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), store,
            Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);

        await worker.RunOnceAsync(CancellationToken.None);

        store.Current.Should().BeSameAs(previous);
    }
}
```

- [ ] **Step 7: Add `GrpcCollectorServiceTests.cs`**

`tests/OpenRussiaRouter.CollectorService.Tests/Grpc/GrpcCollectorServiceTests.cs`:

```csharp
using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Infrastructure.Grpc;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.CollectorService.Tests.Grpc;

public class GrpcCollectorServiceTests
{
    private static GrpcCollectorService Build(CollectorSnapshot snapshot)
    {
        var store = new CollectorSnapshotStore();
        store.Set(snapshot);
        return new GrpcCollectorService(store);
    }

    [Fact]
    public async Task GetCapabilitiesSnapshot_ProjectsNodesAndTimestamp()
    {
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("n1", false,
                [new ModelObservation { UpstreamId = "u", PublicId = "p", Rank = 2 }], null, null)],
            DateTimeOffset.UnixEpoch);
        var service = Build(snapshot);

        var response = await service.GetCapabilitiesSnapshot(new GetCapabilitiesSnapshotRequest(), context: null!);

        response.Nodes.Should().ContainSingle();
        response.Nodes[0].NodeId.Should().Be("n1");
        response.Nodes[0].ModelIds.Should().Equal("p");
        response.CollectedAtUtc.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ListNodeModelCatalog_IncludesUnreachableNodes()
    {
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("dead", true, [], "GET http://dead/x", "HTTP 500")],
            DateTimeOffset.UnixEpoch);
        var service = Build(snapshot);

        var response = await service.ListNodeModelCatalog(new ListNodeModelCatalogRequest(), context: null!);

        response.Entries.Should().BeEmpty();
        response.UnreachableNodeIds.Should().Equal("dead");
        response.UnreachableNodes.Should().ContainSingle(u => u.NodeId == "dead" && u.Response == "HTTP 500");
    }

    [Fact]
    public async Task GetNodeModels_UnknownNode_ReturnsError()
    {
        var service = Build(CollectorSnapshot.Empty);

        var response = await service.GetNodeModels(new GetNodeModelsRequest { NodeId = "nope" }, context: null!);

        response.Error.Should().NotBeEmpty();
        response.PublicIds.Should().BeEmpty();
    }
}
```

- [ ] **Step 8: Restore, build, run the tests**

Run:
```bash
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test tests/OpenRussiaRouter.CollectorService.Tests --no-build -nr:false -m:1
```
Expected: all CollectorService tests PASS.

- [ ] **Step 9: Commit**

```bash
git add tests/OpenRussiaRouter.CollectorService.Tests/ OpenRussiaRouter.sln
git commit -m "test(collector): add unit tests for snapshot, projection, collector, worker, grpc"
```

---

## Task 10: Dockerfile + compose + kustomize

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Dockerfile`
- Modify: `docker-compose.yml`
- Create: `infra/kustomize/base/services/collector.yaml`
- Modify: `infra/kustomize/base/services/kustomization.yaml`

- [ ] **Step 1: Create the Dockerfile**

`src/OpenRussiaRouter.CollectorService/Dockerfile` (mirrors ControlPlane's Dockerfile, minus the `db/` copy):

```dockerfile
# syntax=docker/dockerfile:1.7
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0

FROM ${SDK_IMAGE} AS build
WORKDIR /src

COPY NuGet.Config Directory.Packages.props ./
COPY src/OpenRussiaRouter.BuildingBlocks/OpenRussiaRouter.BuildingBlocks.csproj src/OpenRussiaRouter.BuildingBlocks/
COPY src/OpenRussiaRouter.Contracts/OpenRussiaRouter.Contracts.csproj src/OpenRussiaRouter.Contracts/
COPY src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj src/OpenRussiaRouter.CollectorService/
RUN dotnet restore src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj --configfile NuGet.Config

COPY src/ src/
RUN dotnet publish src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj \
    -c Release -o /app/publish --no-restore

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenRussiaRouter.CollectorService.dll"]
```

- [ ] **Step 2: Add the compose service**

In `docker-compose.yml`, add a `collector` service block after the `control-plane` block (mirror its shape; the collector needs HTTP/2 for gRPC and points at control-plane):

```yaml
  collector:
    build:
      context: .
      dockerfile: src/OpenRussiaRouter.CollectorService/Dockerfile
    restart: unless-stopped
    depends_on:
      control-plane:
        condition: service_started
    ports:
      - "5005:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Kestrel__Endpoints__Grpc__Url: "http://+:8080"
      Kestrel__Endpoints__Grpc__Protocols: "Http2"
      GrpcServices__ControlPlane: "http://control-plane:8080"
      Seq__Url: "http://seq:80"
```

- [ ] **Step 3: Create the kustomize manifest**

`infra/kustomize/base/services/collector.yaml` (mirror `control-plane.yaml`, but gRPC-only, no DB, no health-path env):

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: collector
  namespace: orr-dev
spec:
  replicas: 1
  selector:
    matchLabels:
      app: collector
  template:
    metadata:
      labels:
        app: collector
    spec:
      containers:
        - name: collector
          image: ghcr.io/denduka/collector:latest
          ports:
            - containerPort: 8080
            - containerPort: 8081
          env:
            - name: ASPNETCORE_URLS
              value: "http://+:8080"
            - name: Kestrel__Endpoints__Http__Url
              value: "http://+:8080"
            - name: Kestrel__Endpoints__Http__Protocols
              value: "Http1"
            - name: Kestrel__Endpoints__Grpc__Url
              value: "http://+:8081"
            - name: Kestrel__Endpoints__Grpc__Protocols
              value: "Http2"
            - name: Seq__Url
              value: "http://seq:5341"
            - name: Seq__ServiceName
              value: "collector"
            - name: GrpcServices__ControlPlane
              value: "http://control-plane:8080"
          readinessProbe:
            httpGet:
              path: /health
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 10
          livenessProbe:
            httpGet:
              path: /health
              port: 8080
            initialDelaySeconds: 15
            periodSeconds: 20
          resources:
            requests:
              cpu: 100m
              memory: 128Mi
            limits:
              cpu: 500m
              memory: 512Mi
---
apiVersion: v1
kind: Service
metadata:
  name: collector
  namespace: orr-dev
spec:
  ports:
    - port: 8080
      targetPort: 8081
  selector:
    app: collector
```

- [ ] **Step 4: Register the manifest in kustomization**

In `infra/kustomize/base/services/kustomization.yaml`, add `- collector.yaml` to the `resources` list (e.g. after `control-plane.yaml`).

- [ ] **Step 5: Validate compose + kustomize**

Run:
```bash
docker compose config -q
kubectl kustomize infra/kustomize/base > $null
```
Expected: both commands succeed with no output (compose config validates; kustomize renders). If `docker`/`kubectl` are unavailable in the environment, skip this step and rely on review.

- [ ] **Step 6: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Dockerfile docker-compose.yml infra/kustomize/base/services/collector.yaml infra/kustomize/base/services/kustomization.yaml
git commit -m "build(collector): add Dockerfile, compose service, and kustomize manifest"
```

---

## Task 11: Final verification

- [ ] **Step 1: Full build + full test run**

Run:
```bash
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```
Expected: solution builds; all tests (existing + new collector tests) pass. No existing service behavior changed.

- [ ] **Step 2: Smoke-run the collector locally (optional, requires ControlPlane running)**

Run ControlPlane in one shell (`dotnet run --project src/OpenRussiaRouter.ControlPlane`), then:
```bash
dotnet run --project src/OpenRussiaRouter.CollectorService
```
Expected: the collector starts, logs a poll cycle, and serves gRPC on `http://localhost:5005`. With no nodes registered it serves an empty snapshot. (gRPC can be exercised with `grpcurl` if available; otherwise this is validated by the unit tests.)

- [ ] **Step 3: Confirm no regressions in existing tests**

The collector runs in parallel and is not yet consumed by any service, so ControlPlane/WebApp behavior is unchanged. Phase 2 adds quota + OpenRouter collection; Phase 3 cuts consumers over.

---

## Self-Review Notes

- **Spec coverage (Phase 1 slice):** new service + port 5005 (Task 2), node list via `ListRouterNodes` (Task 7), capabilities/catalog/node-models RPCs (Task 1, 8), in-memory snapshot rebuilt on restart (Task 4), keep-stale on failure (Task 6 collector + Task 7 worker), graceful degradation when ControlPlane unreachable (Task 7), infra (Task 10). Quota, OpenRouter, and consumer cutover are explicitly Phase 2/3.
- **Intentional duplication:** read-only node-admin DTOs/client are copied (not shared) to keep Phase 1 from touching ControlPlane; Phase 3 revisits.
- **Type consistency:** `CollectorNode`, `NodeCatalogResult`, `ModelObservation`, `CollectorSnapshot`, `CatalogEntry`, and the proto messages are used identically across tasks.
