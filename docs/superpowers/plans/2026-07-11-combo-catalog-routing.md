# Combo Catalog Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface live 9Router Combo aliases in the node catalog so model cards can route through Combo names.

**Architecture:** The Collector augments the provider model catalog with observations read from the node's authenticated `/api/combos` endpoint. Each Combo is a routable capability whose public and upstream IDs are the Combo name. Existing WebApp and ControlPlane consumers then receive the aliases through the unchanged Collector gRPC catalog.

**Tech Stack:** .NET 8, C#, `HttpClient`, `System.Text.Json`, xUnit, Moq, FluentAssertions.

---

## File structure

- `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/INodeAdminClient.cs` — Combo API contract.
- `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminClient.cs` — authenticated HTTP request.
- `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminContracts.cs` — Combo response DTOs.
- `src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogCollector.cs` — merge valid Combo aliases with provider observations.
- `tests/OpenRussiaRouter.CollectorService.Tests/Collection/NodeCatalogCollectorTests.cs` — happy path and graceful-failure tests.

### Task 1: Add the node Combo API contract

**Files:**
- Modify: `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/INodeAdminClient.cs`
- Modify: `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminClient.cs`
- Modify: `src/OpenRussiaRouter.CollectorService/Application/NodeAdmin/NodeAdminContracts.cs`

- [ ] **Step 1: Add a failing collector test that configures `ListCombosAsync`**

```csharp
client.Setup(x => x.ListCombosAsync(node.EndpointUrl, node.ControlToken, It.IsAny<CancellationToken>()))
    .ReturnsAsync(new NodeCombosResponse
    {
        Combos = [new NodeComboInfo { Name = "deepseek-v4-flash", Models = ["ocg/deepseek-v4-flash"] }]
    });
```

- [ ] **Step 2: Run the focused test and verify compilation fails**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --no-restore -nr:false -m:1 --filter FullyQualifiedName~NodeCatalogCollectorTests`

Expected: FAIL because `ListCombosAsync` and `NodeCombosResponse` do not exist.

- [ ] **Step 3: Add the minimal API contract and DTOs**

```csharp
public Task<NodeCombosResponse> ListCombosAsync(Uri endpoint, string controlToken, CancellationToken cancellationToken);

public sealed record NodeCombosResponse
{
    public IReadOnlyList<NodeComboInfo> Combos { get; init; } = [];
}

public sealed record NodeComboInfo
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Models { get; init; } = [];
}
```

`NodeAdminClient` must call `SendAsync<NodeCombosResponse>(endpoint, controlToken, "/api/combos", cancellationToken)` and expose a matching diagnostic request description.

- [ ] **Step 4: Run the focused test**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --no-restore -nr:false -m:1 --filter FullyQualifiedName~NodeCatalogCollectorTests`

Expected: the new test compiles and remains red until Task 2 merges the response.

### Task 2: Project Combos into node capabilities

**Files:**
- Modify: `src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogCollector.cs`
- Test: `tests/OpenRussiaRouter.CollectorService.Tests/Collection/NodeCatalogCollectorTests.cs`

- [ ] **Step 1: Write the failing capability test**

```csharp
var result = await Build(client).CollectAsync(node, CancellationToken.None);

result.Observations.Should().ContainSingle(o =>
    o.UpstreamId == "deepseek-v4-flash" &&
    o.PublicId == "deepseek-v4-flash" &&
    o.DisplayName == "deepseek-v4-flash");
```

- [ ] **Step 2: Implement the minimal merge**

After provider observations are collected, call `ListCombosAsync`. For each non-empty unique Combo name add:

```csharp
new ModelObservation
{
    UpstreamId = combo.Name.Trim(),
    PublicId = combo.Name.Trim(),
    DisplayName = combo.Name.Trim(),
    Rank = 3
}
```

Do not fabricate provider, modality, quota, or reasoning metadata for a Combo.

- [ ] **Step 3: Write the graceful-failure test**

```csharp
client.Setup(x => x.ListCombosAsync(node.EndpointUrl, node.ControlToken, It.IsAny<CancellationToken>()))
    .ThrowsAsync(new NodeAdminException(HttpStatusCode.NotFound, "missing"));

var result = await Build(client).CollectAsync(node, CancellationToken.None);

result.Unreachable.Should().BeFalse();
result.Observations.Should().Contain(o => o.PublicId == "ocg/deepseek-v4-flash");
```

- [ ] **Step 4: Handle Combo endpoint failure without invalidating the node**

Catch `NodeAdminException`, `HttpRequestException`, and transient timeout failures around the Combo request, log a warning, and return the provider observations already collected.

- [ ] **Step 5: Run Collector tests**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --no-restore -nr:false -m:1`

Expected: PASS.

### Task 3: Build and verify catalog consumers

**Files:**
- No additional production files.

- [ ] **Step 1: Build the solution**

Run: `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1`

Expected: PASS with no warnings.

- [ ] **Step 2: Verify capability projection**

Add or extend `CatalogProjectionTests` with a snapshot containing a Combo observation and assert `CatalogProjection.Capabilities` returns the Combo name for its node.

- [ ] **Step 3: Run the focused projection test**

Run: `dotnet test tests/OpenRussiaRouter.CollectorService.Tests --no-build -nr:false -m:1 --filter FullyQualifiedName~CatalogProjectionTests`

Expected: PASS.

### Task 4: Update live model cards after deployment

**Files:**
- No repository files; use `https://openrussiarouter.com/admin/models`.

- [ ] **Step 1: Wait for the Collector poll after the new service image is deployed**

Verify the Router Model dropdown contains all 18 aliases:
`deepseek-v4-flash`, `deepseek-v4-pro`, `glm-5.1`, `glm-5.2`, `kimi-k2.6`, `kimi-k2.7-code`, `mimo-v2.5`, `mimo-v2.5-pro`, `minimax-m2.5`, `minimax-m2.7`, `minimax-m3`, `qwen3.6-plus`, `qwen3.7-max`, `qwen3.7-plus`, `glm-5`, `gemini-3.1-pro`, `gemini-3.5-flash`, `gemini-3-flash`.

- [ ] **Step 2: Edit each corresponding card**

Set only `Router Model` to its matching Combo alias; retain the curated names, descriptions, prices, context, and modalities already entered.

- [ ] **Step 3: Verify routing**

Use the admin gateway test or a minimal chat completion for one Combo-backed card and confirm the selected node receives the Combo alias.
