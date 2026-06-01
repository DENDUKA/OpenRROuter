# Collector Service — Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the CollectorService (built in Phase 1) to also collect **provider quota** (served as the OpenTelemetry gauge `orr_provider_quota` plus a `GetProviderQuota` RPC) and the **OpenRouter catalog** (served via a `GetOpenRouterCatalog` RPC) — still in parallel, without changing existing service behavior.

**Architecture:** Quota is collected in the same per-node poll cycle as the model catalog (one extra node-admin call per node) into a separate `QuotaSnapshotStore`; an observable gauge reads that store. The OpenRouter catalog is collected by an independent `BackgroundService` on a longer interval into an `OpenRouterCatalogStore`. Two new RPCs project these stores.

**Tech Stack:** Same as Phase 1 (.NET 8, gRPC, OpenTelemetry/Prometheus, xUnit/FluentAssertions/Moq), plus `System.Diagnostics.Metrics`.

**Prerequisite:** Phase 1 merged. **Spec:** `docs/superpowers/specs/2026-06-01-collector-service-design.md`.

---

## File Structure

**Contract (modify):** `src/OpenRussiaRouter.Contracts/Protos/collector.proto` — add quota + OpenRouter RPCs/messages.

**New files in `src/OpenRussiaRouter.CollectorService/`:**
- `Application/NodeAdmin/NodeAdminContracts.cs` (modify) — add quota DTOs.
- `Application/NodeAdmin/INodeAdminClient.cs` + `NodeAdminClient.cs` (modify) — add `GetProviderQuotaAsync`.
- `Application/Quota/QuotaMeasurement.cs`, `QuotaSnapshot.cs`, `QuotaSnapshotStore.cs`, `NodeQuotaCollector.cs`, `ProviderQuotaMeter.cs`.
- `Application/OpenRouter/OpenRouterCatalogContracts.cs`, `OpenRouterCatalogStore.cs`, `OpenRouterCatalogOptions.cs`, `OpenRouterCatalogClient.cs`, `OpenRouterCatalogPoller.cs`.
- `Application/Collection/NodePollingWorker.cs` (modify) — also collect quota.
- `Infrastructure/Grpc/GrpcCollectorService.cs` (modify) — add `GetProviderQuota` + `GetOpenRouterCatalog`.
- `Infrastructure/Startup/Startup.cs` (modify) — register new services.
- `appsettings.json` (modify) — OpenRouter config.

**New tests in `tests/OpenRussiaRouter.CollectorService.Tests/`:**
- `Quota/NodeQuotaCollectorTests.cs`, `Quota/ProviderQuotaMeterTests.cs`
- `OpenRouter/OpenRouterCatalogClientTests.cs`
- `Grpc/GrpcCollectorServiceQuotaTests.cs`

---

## Task 1: Extend the contract with quota + OpenRouter

**Files:**
- Modify: `src/OpenRussiaRouter.Contracts/Protos/collector.proto`

- [ ] **Step 1: Add RPCs and messages**

Add the two RPCs to the `service Collector` block:

```proto
  rpc GetProviderQuota (GetProviderQuotaRequest) returns (GetProviderQuotaResponse);
  rpc GetOpenRouterCatalog (GetOpenRouterCatalogRequest) returns (GetOpenRouterCatalogResponse);
```

Append these messages:

```proto
message GetProviderQuotaRequest {}

message ProviderQuotaMeasurement {
  string node_id = 1;
  string provider = 2;
  string connection_id = 3;
  double remaining = 4;
}

message GetProviderQuotaResponse {
  repeated ProviderQuotaMeasurement measurements = 1;
  string collected_at_utc = 2;
}

message GetOpenRouterCatalogRequest {}

message OpenRouterCatalogEntry {
  string slug = 1;
  string name = 2;
  string author_display_name = 3;
  string description = 4;
  int32 context_length = 5;
  repeated string input_modalities = 6;
  // Per-token prices as raw strings from OpenRouter (USD per single token); empty if absent.
  string price_prompt = 7;
  string price_completion = 8;
  string price_input_cache_read = 9;
}

message GetOpenRouterCatalogResponse {
  repeated OpenRouterCatalogEntry entries = 1;
  string collected_at_utc = 2;
  // False when the catalog has never been successfully fetched (consumers keep their own fallback).
  bool available = 3;
}
```

- [ ] **Step 2: Build the contract**

Run: `dotnet build src/OpenRussiaRouter.Contracts/OpenRussiaRouter.Contracts.csproj -nr:false -m:1`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.Contracts/Protos/collector.proto
git commit -m "feat(contracts): add quota and OpenRouter RPCs to collector.proto"
```

---

## Task 2: Provider quota — node-admin client method + DTOs

**Files:**
- Modify: `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminContracts.cs`
- Modify: `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/INodeAdminClient.cs`
- Modify: `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminClient.cs`

- [ ] **Step 1: Add quota DTOs**

Append to `NodeAdminContracts.cs`:

```csharp
public sealed record NodeProviderQuotaResponse
{
    public IReadOnlyList<NodeProviderQuotaEntry> Providers { get; init; } = [];
}

public sealed record NodeProviderQuotaEntry
{
    public string ConnectionId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool QuotaSupported { get; init; }
    public System.Text.Json.JsonElement? Usage { get; init; }
    public string? Error { get; init; }
}
```

- [ ] **Step 2: Add the interface method**

Add to `INodeAdminClient`:

```csharp
    Task<NodeProviderQuotaResponse> GetProviderQuotaAsync(
        Uri endpoint, string controlToken, bool includeInactive, CancellationToken cancellationToken);
```

- [ ] **Step 3: Implement it**

In `NodeAdminClient.cs`, add the path constant and method (mirrors `RouterNodeProvider.GetProviderQuotaAsync`):

```csharp
    private const string ProviderQuotaPath = "/api/integrations/providers/quota";

    public Task<NodeProviderQuotaResponse> GetProviderQuotaAsync(
        Uri endpoint, string controlToken, bool includeInactive, CancellationToken cancellationToken)
    {
        var path = includeInactive ? $"{ProviderQuotaPath}?includeInactive=true" : ProviderQuotaPath;
        return SendAsync<NodeProviderQuotaResponse>(endpoint, controlToken, path, cancellationToken);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/OpenRussiaRouter.CollectorService/OpenRussiaRouter.CollectorService.csproj -nr:false -m:1`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/
git commit -m "feat(collector): add provider quota to node-admin client"
```

---

## Task 3: Quota snapshot, collector, and store

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Application/Quota/QuotaMeasurement.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/Quota/QuotaSnapshot.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/Quota/QuotaSnapshotStore.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/Quota/NodeQuotaCollector.cs`
- Test: `tests/.../Quota/NodeQuotaCollectorTests.cs`

- [ ] **Step 1: Create measurement + snapshot + store**

`Application/Quota/QuotaMeasurement.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record QuotaMeasurement(string NodeId, string Provider, string ConnectionId, double Remaining);
```

`Application/Quota/QuotaSnapshot.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record QuotaSnapshot(
    IReadOnlyList<QuotaMeasurement> Measurements,
    DateTimeOffset? CollectedAtUtc)
{
    public static readonly QuotaSnapshot Empty = new([], null);
}
```

`Application/Quota/QuotaSnapshotStore.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed class QuotaSnapshotStore
{
    private volatile QuotaSnapshot _current = QuotaSnapshot.Empty;
    public QuotaSnapshot Current => _current;
    public void Set(QuotaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = snapshot;
    }
}
```

- [ ] **Step 2: Write the failing test for the per-node quota collector**

`tests/OpenRussiaRouter.CollectorService.Tests/Quota/NodeQuotaCollectorTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;
using OpenRussiaRouter.CollectorService.Application.Quota;
using OpenRussiaRouter.CollectorService.Tests.Collection; // OptionsTestExtensions

namespace OpenRussiaRouter.CollectorService.Tests.Quota;

public class NodeQuotaCollectorTests
{
    private static CollectorNode Node() =>
        new("n1", new Uri("http://node/"), "tok", true, "Healthy", true);

    private static NodeQuotaCollector Build(INodeAdminClient client) =>
        new(client, Options.Create(new CollectorOptions()).ToMonitor(), NullLogger<NodeQuotaCollector>.Instance);

    [Fact]
    public async Task CollectAsync_ExtractsRemaining_FromUsageJson()
    {
        using var doc = JsonDocument.Parse("""{"remaining": 1234.5}""");
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers =
                [
                    new NodeProviderQuotaEntry { ConnectionId = "c1", Provider = "openai", Usage = doc.RootElement.Clone() }
                ]
            });

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Should().Be(new QuotaMeasurement("n1", "openai", "c1", 1234.5));
    }

    [Fact]
    public async Task CollectAsync_SkipsEntriesWithoutUsage_AndSwallowsErrors()
    {
        var client = new Mock<INodeAdminClient>();
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NodeAdminException(System.Net.HttpStatusCode.InternalServerError, "boom"));

        var result = await Build(client.Object).CollectAsync(Node(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --filter NodeQuotaCollectorTests -nr:false -m:1`
Expected: FAIL — `NodeQuotaCollector` does not exist.

- [ ] **Step 4: Implement the per-node quota collector**

`Application/Quota/NodeQuotaCollector.cs` (ports the usage-parsing from `ProviderQuotaCollectorWorker.CollectQuotasAsync`):

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;

namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed class NodeQuotaCollector(
    INodeAdminClient client,
    IOptionsMonitor<CollectorOptions> options,
    ILogger<NodeQuotaCollector> logger)
{
    public async Task<IReadOnlyList<QuotaMeasurement>> CollectAsync(CollectorNode node, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.CurrentValue.PerNodeTimeout);

        NodeProviderQuotaResponse response;
        try
        {
            response = await client
                .GetProviderQuotaAsync(node.EndpointUrl, node.ControlToken, includeInactive: false, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Quota collection failed for node {NodeId}", node.Id);
            return [];
        }

        var measurements = new List<QuotaMeasurement>();
        foreach (var provider in response.Providers)
        {
            if (provider.Usage is not { } usage || usage.ValueKind == System.Text.Json.JsonValueKind.Null)
                continue;
            if (!usage.TryGetProperty("remaining", out var remainingElement))
                continue;
            if (remainingElement.ValueKind is not (System.Text.Json.JsonValueKind.Number))
                continue;

            measurements.Add(new QuotaMeasurement(
                node.Id,
                string.IsNullOrWhiteSpace(provider.Provider) ? "unknown" : provider.Provider,
                string.IsNullOrWhiteSpace(provider.ConnectionId) ? "unknown" : provider.ConnectionId,
                remainingElement.GetDouble()));
        }
        return measurements;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --filter NodeQuotaCollectorTests -nr:false -m:1`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Quota/ tests/OpenRussiaRouter.CollectorService.Tests/Quota/NodeQuotaCollectorTests.cs
git commit -m "feat(collector): add provider quota collection and snapshot store"
```

---

## Task 4: Provider quota OTel gauge

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Application/Quota/ProviderQuotaMeter.cs`
- Test: `tests/.../Quota/ProviderQuotaMeterTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/OpenRussiaRouter.CollectorService.Tests/Quota/ProviderQuotaMeterTests.cs`:

```csharp
using System.Diagnostics.Metrics;
using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Quota;

namespace OpenRussiaRouter.CollectorService.Tests.Quota;

public class ProviderQuotaMeterTests
{
    [Fact]
    public void Gauge_PublishesMeasurementsFromStore()
    {
        var store = new QuotaSnapshotStore();
        store.Set(new QuotaSnapshot([new QuotaMeasurement("n1", "openai", "c1", 42d)], DateTimeOffset.UnixEpoch));

        using var meter = new ProviderQuotaMeter(store);
        var collected = new List<Measurement<double>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "OpenRussiaRouter" && instrument.Name == "orr_provider_quota")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            collected.Add(new Measurement<double>(value, tags.ToArray())));
        listener.Start();

        listener.RecordObservableInstruments();

        collected.Should().ContainSingle(m => m.Value == 42d);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --filter ProviderQuotaMeterTests -nr:false -m:1`
Expected: FAIL — `ProviderQuotaMeter` does not exist.

- [ ] **Step 3: Implement the meter**

`Application/Quota/ProviderQuotaMeter.cs` (ports the gauge from `ProviderQuotaCollectorWorker`; meter name `OpenRussiaRouter` matches the meter already registered in `Program.cs`):

```csharp
using System.Diagnostics.Metrics;

namespace OpenRussiaRouter.CollectorService.Application.Quota;

/// <summary>Exposes the latest quota snapshot as the observable gauge `orr_provider_quota`.</summary>
public sealed class ProviderQuotaMeter : IDisposable
{
    private readonly Meter _meter;

    public ProviderQuotaMeter(QuotaSnapshotStore store)
    {
        _meter = new Meter("OpenRussiaRouter");
        _meter.CreateObservableGauge(
            "orr_provider_quota",
            () => store.Current.Measurements.Select(m => new Measurement<double>(
                m.Remaining,
                new KeyValuePair<string, object?>("node_id", m.NodeId),
                new KeyValuePair<string, object?>("provider", m.Provider),
                new KeyValuePair<string, object?>("connection_id", m.ConnectionId))),
            unit: "tokens",
            description: "Remaining quota for providers");
    }

    public void Dispose() => _meter.Dispose();
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --filter ProviderQuotaMeterTests -nr:false -m:1`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Quota/ProviderQuotaMeter.cs tests/OpenRussiaRouter.CollectorService.Tests/Quota/ProviderQuotaMeterTests.cs
git commit -m "feat(collector): expose orr_provider_quota OTel gauge"
```

---

## Task 5: Wire quota into the poll worker

**Files:**
- Modify: `src/OpenRussiaRouter.CollectorService/Application/Collection/NodePollingWorker.cs`
- Test: `tests/.../Collection/NodePollingWorkerTests.cs` (add a case)

- [ ] **Step 1: Add quota collection to the worker**

Update `NodePollingWorker`'s constructor to inject `NodeQuotaCollector quotaCollector` and `QuotaSnapshotStore quotaStore`, and update `RunOnceAsync` so that after computing `routable` it collects both catalog and quota and sets both stores. Replace the body after the `routable` line with:

```csharp
        var now = timeProvider.GetUtcNow();

        var catalogResults = await Task.WhenAll(
            routable.Select(node => collector.CollectAsync(node, cancellationToken))).ConfigureAwait(false);
        store.Set(new CollectorSnapshot(catalogResults, now));

        var quotaResults = await Task.WhenAll(
            routable.Select(node => quotaCollector.CollectAsync(node, cancellationToken))).ConfigureAwait(false);
        quotaStore.Set(new QuotaSnapshot(quotaResults.SelectMany(m => m).ToList(), now));
```

Add `using OpenRussiaRouter.CollectorService.Application.Quota;` to the file. The full updated constructor signature:

```csharp
public sealed class NodePollingWorker(
    INodeListProvider nodeListProvider,
    NodeCatalogCollector collector,
    NodeQuotaCollector quotaCollector,
    CollectorSnapshotStore store,
    QuotaSnapshotStore quotaStore,
    IOptionsMonitor<CollectorOptions> options,
    ILogger<NodePollingWorker> logger,
    TimeProvider timeProvider)
    : BackgroundService
```

- [ ] **Step 2: Update existing worker tests for the new constructor**

In `tests/.../Collection/NodePollingWorkerTests.cs`, update both existing `new NodePollingWorker(...)` calls to pass the new arguments. Add a shared helper at the top of the class:

```csharp
    private static NodeQuotaCollector QuotaCollector(INodeAdminClient client) =>
        new(client, Options.Create(new CollectorOptions()).ToMonitor(), NullLogger<NodeQuotaCollector>.Instance);
```

Update the first test's worker construction (and add a `QuotaSnapshotStore quotaStore = new();` local) to:

```csharp
        var quotaStore = new QuotaSnapshotStore();
        var worker = new NodePollingWorker(
            nodes.Object, Collector(client.Object), QuotaCollector(client.Object), store, quotaStore,
            Options.Create(new CollectorOptions()).ToMonitor(),
            NullLogger<NodePollingWorker>.Instance, TimeProvider.System);
```

Apply the same change to the second test (the keep-previous-snapshot test), adding `var quotaStore = new QuotaSnapshotStore();`. Add `using OpenRussiaRouter.CollectorService.Application.Quota;`.

- [ ] **Step 3: Add a quota assertion to the first worker test**

In `RunOnceAsync_PollsRoutableNodes_AndSwapsSnapshot`, set up the mock to also return quota and assert it lands in `quotaStore`. After the existing `client.Setup(... ListProvidersWithModelsAsync ...)`, add:

```csharp
        using var usage = System.Text.Json.JsonDocument.Parse("""{"remaining": 7}""");
        client.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers = [new NodeProviderQuotaEntry { ConnectionId = "c1", Provider = "openai", Usage = usage.RootElement.Clone() }]
            });
```

And after the existing assertions add:

```csharp
        quotaStore.Current.Measurements.Should().ContainSingle(m => m.NodeId == "n1" && m.Remaining == 7d);
```

- [ ] **Step 4: Run the worker tests**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --filter NodePollingWorkerTests -nr:false -m:1`
Expected: PASS (after registering the new services in Startup — Task 7 — the full build passes; for this isolated test run the test project references the service project directly, so it builds).

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Collection/NodePollingWorker.cs tests/OpenRussiaRouter.CollectorService.Tests/Collection/NodePollingWorkerTests.cs
git commit -m "feat(collector): collect provider quota in the poll cycle"
```

---

## Task 6: OpenRouter catalog poller

**Files:**
- Create: `src/OpenRussiaRouter.CollectorService/Application/OpenRouter/OpenRouterCatalogContracts.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/OpenRouter/OpenRouterCatalogStore.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/OpenRouter/OpenRouterCatalogOptions.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/OpenRouter/OpenRouterCatalogClient.cs`
- Create: `src/OpenRussiaRouter.CollectorService/Application/OpenRouter/OpenRouterCatalogPoller.cs`
- Test: `tests/.../OpenRouter/OpenRouterCatalogClientTests.cs`

- [ ] **Step 1: Create the DTOs + store + options**

`Application/OpenRouter/OpenRouterCatalogContracts.cs` (raw JSON shape copied from WebApp's `OpenRouterModelDto.cs`, plus a flattened in-memory `OpenRouterEntry` the store/RPC use):

```csharp
using System.Text.Json.Serialization;

namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed record OpenRouterModelsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<OpenRouterModelDto> Data);

public sealed record OpenRouterModelDto(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("author_display_name")] string? AuthorDisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("context_length")] int? ContextLength,
    [property: JsonPropertyName("input_modalities")] IReadOnlyList<string>? InputModalities,
    [property: JsonPropertyName("endpoint")] OpenRouterEndpointDto? Endpoint);

public sealed record OpenRouterEndpointDto(
    [property: JsonPropertyName("pricing")] OpenRouterPricingDto? Pricing);

public sealed record OpenRouterPricingDto(
    [property: JsonPropertyName("prompt")] string? Prompt,
    [property: JsonPropertyName("completion")] string? Completion,
    [property: JsonPropertyName("input_cache_read")] string? InputCacheRead);

/// <summary>Flattened catalog entry held in-memory and projected into the gRPC response.</summary>
public sealed record OpenRouterEntry(
    string Slug,
    string Name,
    string AuthorDisplayName,
    string Description,
    int ContextLength,
    IReadOnlyList<string> InputModalities,
    string PricePrompt,
    string PriceCompletion,
    string PriceInputCacheRead);
```

`Application/OpenRouter/OpenRouterCatalogStore.cs`:

```csharp
namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed record OpenRouterCatalogSnapshot(
    IReadOnlyList<OpenRouterEntry> Entries,
    DateTimeOffset? CollectedAtUtc,
    bool Available)
{
    public static readonly OpenRouterCatalogSnapshot Empty = new([], null, false);
}

public sealed class OpenRouterCatalogStore
{
    private volatile OpenRouterCatalogSnapshot _current = OpenRouterCatalogSnapshot.Empty;
    public OpenRouterCatalogSnapshot Current => _current;
    public void Set(OpenRouterCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = snapshot;
    }
}
```

`Application/OpenRouter/OpenRouterCatalogOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed class OpenRouterCatalogOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:01:00", "06:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(30);
}
```

- [ ] **Step 2: Write the failing test for the client mapping**

`tests/OpenRussiaRouter.CollectorService.Tests/OpenRouter/OpenRouterCatalogClientTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.CollectorService.Application.OpenRouter;

namespace OpenRussiaRouter.CollectorService.Tests.OpenRouter;

public class OpenRouterCatalogClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static OpenRouterCatalogClient Build(HttpStatusCode status, string body)
    {
        var http = new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("https://openrouter.ai/") };
        return new OpenRouterCatalogClient(http, NullLogger<OpenRouterCatalogClient>.Instance);
    }

    [Fact]
    public async Task LoadAsync_FlattensEntries_AndPricing()
    {
        const string body = """
        {"data":[{"slug":"openai/gpt-4o","name":"OpenAI: GPT-4o","author_display_name":"OpenAI",
        "description":"desc","context_length":128000,"input_modalities":["text","image"],
        "endpoint":{"pricing":{"prompt":"0.000005","completion":"0.000015","input_cache_read":"0.0000025"}}}]}
        """;
        var entries = await Build(HttpStatusCode.OK, body).LoadAsync(CancellationToken.None);

        entries.Should().ContainSingle();
        var e = entries[0];
        e.Slug.Should().Be("openai/gpt-4o");
        e.ContextLength.Should().Be(128000);
        e.InputModalities.Should().Equal("text", "image");
        e.PricePrompt.Should().Be("0.000005");
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_OnHttpError()
    {
        var entries = await Build(HttpStatusCode.InternalServerError, "boom").LoadAsync(CancellationToken.None);
        entries.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --filter OpenRouterCatalogClientTests -nr:false -m:1`
Expected: FAIL — `OpenRouterCatalogClient` does not exist.

- [ ] **Step 4: Implement the client**

`Application/OpenRouter/OpenRouterCatalogClient.cs` (fetch from `api/frontend/models`, flatten; mirrors WebApp's `LoadCatalogAsync`):

```csharp
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed class OpenRouterCatalogClient(HttpClient httpClient, ILogger<OpenRouterCatalogClient> logger)
{
    public async Task<IReadOnlyList<OpenRouterEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<OpenRouterModelsResponse>(
                "api/frontend/models", cancellationToken).ConfigureAwait(false);
            var data = response?.Data ?? [];
            return data
                .Where(d => !string.IsNullOrWhiteSpace(d.Slug))
                .Select(d => new OpenRouterEntry(
                    d.Slug,
                    d.Name ?? string.Empty,
                    d.AuthorDisplayName ?? string.Empty,
                    d.Description ?? string.Empty,
                    d.ContextLength ?? 0,
                    d.InputModalities ?? [],
                    d.Endpoint?.Pricing?.Prompt ?? string.Empty,
                    d.Endpoint?.Pricing?.Completion ?? string.Empty,
                    d.Endpoint?.Pricing?.InputCacheRead ?? string.Empty))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load OpenRouter model catalog");
            return [];
        }
    }
}
```

- [ ] **Step 5: Implement the poller**

`Application/OpenRouter/OpenRouterCatalogPoller.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed class OpenRouterCatalogPoller(
    OpenRouterCatalogClient client,
    OpenRouterCatalogStore store,
    IOptionsMonitor<OpenRouterCatalogOptions> options,
    ILogger<OpenRouterCatalogPoller> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
            return;

        var entries = await client.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            // Keep the previous snapshot on a failed/empty fetch.
            logger.LogWarning("OpenRouter catalog fetch returned no entries; keeping previous snapshot");
            return;
        }
        store.Set(new OpenRouterCatalogSnapshot(entries, timeProvider.GetUtcNow(), Available: true));
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
                logger.LogError(ex, "OpenRouter catalog poll failed");
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

- [ ] **Step 6: Run to verify the client test passes**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --filter OpenRouterCatalogClientTests -nr:false -m:1`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/OpenRouter/ tests/OpenRussiaRouter.CollectorService.Tests/OpenRouter/
git commit -m "feat(collector): add OpenRouter catalog poller and store"
```

---

## Task 7: Project the new stores over gRPC + register services

**Files:**
- Modify: `src/OpenRussiaRouter.CollectorService/Infrastructure/Grpc/GrpcCollectorService.cs`
- Modify: `src/OpenRussiaRouter.CollectorService/Infrastructure/Startup/Startup.cs`
- Modify: `src/OpenRussiaRouter.CollectorService/appsettings.json`
- Test: `tests/.../Grpc/GrpcCollectorServiceQuotaTests.cs`

- [ ] **Step 1: Add the two RPC overrides**

In `GrpcCollectorService`, change the constructor to inject the new stores and add the methods:

```csharp
public sealed class GrpcCollectorService(
    CollectorSnapshotStore store,
    OpenRussiaRouter.CollectorService.Application.Quota.QuotaSnapshotStore quotaStore,
    OpenRussiaRouter.CollectorService.Application.OpenRouter.OpenRouterCatalogStore openRouterStore)
    : Collector.CollectorBase
{
    // ... existing GetCapabilitiesSnapshot / ListNodeModelCatalog / GetNodeModels unchanged ...

    public override Task<GetProviderQuotaResponse> GetProviderQuota(
        GetProviderQuotaRequest request, ServerCallContext context)
    {
        var snapshot = quotaStore.Current;
        var response = new GetProviderQuotaResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty
        };
        foreach (var m in snapshot.Measurements)
        {
            response.Measurements.Add(new ProviderQuotaMeasurement
            {
                NodeId = m.NodeId,
                Provider = m.Provider,
                ConnectionId = m.ConnectionId,
                Remaining = m.Remaining
            });
        }
        return Task.FromResult(response);
    }

    public override Task<GetOpenRouterCatalogResponse> GetOpenRouterCatalog(
        GetOpenRouterCatalogRequest request, ServerCallContext context)
    {
        var snapshot = openRouterStore.Current;
        var response = new GetOpenRouterCatalogResponse
        {
            CollectedAtUtc = snapshot.CollectedAtUtc?.ToString("O") ?? string.Empty,
            Available = snapshot.Available
        };
        foreach (var e in snapshot.Entries)
        {
            var entry = new OpenRouterCatalogEntry
            {
                Slug = e.Slug,
                Name = e.Name,
                AuthorDisplayName = e.AuthorDisplayName,
                Description = e.Description,
                ContextLength = e.ContextLength,
                PricePrompt = e.PricePrompt,
                PriceCompletion = e.PriceCompletion,
                PriceInputCacheRead = e.PriceInputCacheRead
            };
            entry.InputModalities.AddRange(e.InputModalities);
            response.Entries.Add(entry);
        }
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 2: Register the new services in Startup**

In `Startup.ConfigureServices`, add (after the existing singletons):

```csharp
        services.AddSingleton<OpenRussiaRouter.CollectorService.Application.Quota.QuotaSnapshotStore>();
        services.AddSingleton<OpenRussiaRouter.CollectorService.Application.Quota.NodeQuotaCollector>();
        services.AddSingleton<OpenRussiaRouter.CollectorService.Application.Quota.ProviderQuotaMeter>();

        services.AddSingleton<OpenRussiaRouter.CollectorService.Application.OpenRouter.OpenRouterCatalogStore>();
        services.AddOptions<OpenRussiaRouter.CollectorService.Application.OpenRouter.OpenRouterCatalogOptions>()
            .Bind(configuration.GetSection("OpenRouter"))
            .ValidateDataAnnotations();
        services.AddHttpClient<OpenRussiaRouter.CollectorService.Application.OpenRouter.OpenRouterCatalogClient>(c =>
            c.BaseAddress = new Uri(configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/"));
        services.AddHostedService<OpenRussiaRouter.CollectorService.Application.OpenRouter.OpenRouterCatalogPoller>();
```

`ProviderQuotaMeter` is a singleton that must be instantiated at startup so its gauge registers. Add a hosted activation by resolving it eagerly — add this line after `var app = builder.Build();` in `Program.cs`:

```csharp
_ = app.Services.GetRequiredService<OpenRussiaRouter.CollectorService.Application.Quota.ProviderQuotaMeter>();
```

(Add `using Microsoft.Extensions.DependencyInjection;` to `Program.cs` if not already present.)

- [ ] **Step 3: Add OpenRouter config to appsettings.json**

Add to `src/OpenRussiaRouter.CollectorService/appsettings.json` (top-level):

```json
  "OpenRouter": {
    "Enabled": true,
    "BaseUrl": "https://openrouter.ai/",
    "RefreshInterval": "00:30:00"
  }
```

- [ ] **Step 4: Write the gRPC quota/openrouter test**

`tests/OpenRussiaRouter.CollectorService.Tests/Grpc/GrpcCollectorServiceQuotaTests.cs`:

```csharp
using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.OpenRouter;
using OpenRussiaRouter.CollectorService.Application.Quota;
using OpenRussiaRouter.CollectorService.Infrastructure.Grpc;
using OpenRussiaRouter.Contracts.Grpc.Collector.V1;

namespace OpenRussiaRouter.CollectorService.Tests.Grpc;

public class GrpcCollectorServiceQuotaTests
{
    private static GrpcCollectorService Build(QuotaSnapshot quota, OpenRouterCatalogSnapshot openRouter)
    {
        var store = new CollectorSnapshotStore();
        var quotaStore = new QuotaSnapshotStore();
        quotaStore.Set(quota);
        var orStore = new OpenRouterCatalogStore();
        orStore.Set(openRouter);
        return new GrpcCollectorService(store, quotaStore, orStore);
    }

    [Fact]
    public async Task GetProviderQuota_ProjectsMeasurements()
    {
        var service = Build(
            new QuotaSnapshot([new QuotaMeasurement("n1", "openai", "c1", 99d)], DateTimeOffset.UnixEpoch),
            OpenRouterCatalogSnapshot.Empty);

        var response = await service.GetProviderQuota(new GetProviderQuotaRequest(), context: null!);

        response.Measurements.Should().ContainSingle(m => m.NodeId == "n1" && m.Remaining == 99d);
    }

    [Fact]
    public async Task GetOpenRouterCatalog_ProjectsEntriesAndAvailability()
    {
        var entry = new OpenRouterEntry("openai/gpt-4o", "GPT-4o", "OpenAI", "d", 128000, ["text"], "0.000005", "0.000015", "");
        var service = Build(QuotaSnapshot.Empty, new OpenRouterCatalogSnapshot([entry], DateTimeOffset.UnixEpoch, Available: true));

        var response = await service.GetOpenRouterCatalog(new GetOpenRouterCatalogRequest(), context: null!);

        response.Available.Should().BeTrue();
        response.Entries.Should().ContainSingle(e => e.Slug == "openai/gpt-4o" && e.ContextLength == 128000);
    }
}
```

- [ ] **Step 5: Build + run all collector tests**

Run:
```bash
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test tests/OpenRussiaRouter.CollectorService.Tests --no-build -nr:false -m:1
```
Expected: all collector tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/ tests/OpenRussiaRouter.CollectorService.Tests/Grpc/GrpcCollectorServiceQuotaTests.cs
git commit -m "feat(collector): serve quota and OpenRouter catalog over gRPC"
```

---

## Task 8: Final verification

- [ ] **Step 1: Full build + test**

Run:
```bash
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```
Expected: solution builds; all tests pass. Existing ControlPlane/WebApp behavior unchanged (collector still not consumed).

- [ ] **Step 2: Optional smoke run**

`dotnet run --project src/OpenRussiaRouter.CollectorService` with ControlPlane running. Expected: poll cycle collects models + quota; OpenRouter poller fetches the catalog once on startup; `GET http://localhost:5005/metrics` exposes `orr_provider_quota` (the Prometheus endpoint is mapped by `MapOpenTelemetryEndpoints`).

---

## Self-Review Notes

- **Spec coverage (Phase 2 slice):** provider quota collection + OTel gauge on the collector (Tasks 3-5), `GetProviderQuota` RPC (Task 7), OpenRouter catalog collection (Task 6) + `GetOpenRouterCatalog` RPC (Task 7). `FindMatch` deliberately stays in WebApp — the collector serves raw entries only (Task 6 DTOs).
- **Keep-stale:** quota returns empty list per node on failure (Task 3); OpenRouter keeps previous snapshot on empty/failed fetch (Task 6 poller).
- **Type consistency:** `QuotaMeasurement`, `QuotaSnapshot(Store)`, `OpenRouterEntry`, `OpenRouterCatalogSnapshot(Store)`, and the proto messages (`ProviderQuotaMeasurement`, `OpenRouterCatalogEntry`) are used identically across tasks. The `GrpcCollectorService` constructor gains two parameters — Phase 1's `GrpcCollectorServiceTests` construct it with `new GrpcCollectorService(store)`, which will no longer compile; update those constructions to `new GrpcCollectorService(store, new QuotaSnapshotStore(), new OpenRouterCatalogStore())` as part of Task 7 Step 4 (add the `using`s). **Do this before running the suite.**
