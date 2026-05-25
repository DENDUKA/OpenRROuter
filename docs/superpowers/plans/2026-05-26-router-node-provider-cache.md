# Router Node Provider Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cache successful `IRouterNodeProvider` read responses in-memory for 30 seconds by default.

**Architecture:** Add a decorator around the existing HTTP `RouterNodeProvider`. The decorator caches read calls by node and arguments, skips caching failures, and invalidates node entries after successful mutation calls.

**Tech Stack:** .NET 8, ASP.NET Core DI, `IOptionsMonitor<T>`, `ConcurrentDictionary`, xUnit, FluentAssertions.

---

### Task 1: Add Cache Options

**Files:**
- Create: `src/OpenRussiaRouter.ControlPlane/Application/Services/RouterNodeProviderCacheOptions.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Startup/Startup.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/appsettings.json`
- Modify: `src/OpenRussiaRouter.ControlPlane/appsettings.Development.json`
- Modify: `src/OpenRussiaRouter.ControlPlane/appsettings.Production.json`

- [ ] Create options with `Enabled = true` and `Ttl = TimeSpan.FromSeconds(30)`.
- [ ] Bind the `RouterNodeProviderCache` section in ControlPlane startup.
- [ ] Add the section to appsettings files.

### Task 2: Add Cache Decorator

**Files:**
- Create: `src/OpenRussiaRouter.ControlPlane/Application/Services/CachedRouterNodeProvider.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Application/Extensions/ControlPlaneExtensions.cs`

- [ ] Implement `CachedRouterNodeProvider : IRouterNodeProvider`.
- [ ] Cache successful `GetProviderQuotaAsync`, `ListProviderModelsAsync`, `ListIntegrationKeysAsync`, `ListModelsAsync`, and `ListProvidersAsync` calls.
- [ ] Pass through mutation methods and invalidate all cache entries for that node after success.
- [ ] Register `RouterNodeProvider` as the typed HTTP client and `IRouterNodeProvider` as the cache decorator.

### Task 3: Add Focused Tests

**Files:**
- Create: `tests/OpenRussiaRouter.ControlPlane.Tests/Application/CachedRouterNodeProviderTests.cs`

- [ ] Verify repeated read calls return the cached value and call the inner provider once.
- [ ] Verify disabled cache calls the inner provider every time.
- [ ] Verify failed read calls are not cached.
- [ ] Verify successful mutation invalidates cached entries for the same node.

### Task 4: Verify

**Files:**
- Test: `tests/OpenRussiaRouter.ControlPlane.Tests/OpenRussiaRouter.ControlPlane.Tests.csproj`

- [ ] Run `dotnet test tests/OpenRussiaRouter.ControlPlane.Tests --no-build -nr:false -m:1` if build artifacts are fresh.
- [ ] If needed, run `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1`, then rerun the focused tests.
