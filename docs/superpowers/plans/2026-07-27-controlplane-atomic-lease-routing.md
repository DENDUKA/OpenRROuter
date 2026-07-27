# T-007/T-008/T-018 Atomic Lease Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make router lease acquisition and cleanup race-free while avoiding a full router-node table read in the request hot path.

**Architecture:** RouterControlPlaneService filters stable node metadata from a periodically refreshed in-memory snapshot, then delegates dynamic least-load selection and lease mutation to repository methods. PostgreSQL performs selection, increment, insert, release, and expiry cleanup atomically; the in-memory repository implements the same observable contract for unit tests.

**Tech Stack:** .NET 8, Dapper, Npgsql/PostgreSQL CTEs, xUnit, FluentAssertions, Testcontainers.

---

### Task 1: Add repository operations with atomic semantics

**Files:**
- Modify: `src/OpenRussiaRouter.ControlPlane/Application/Abstractions/Persistence/IControlPlaneRepository.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Repository/ControlPlaneRepository.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Repository/InMemoryControlPlaneRepository.cs`

- [ ] **Step 1: Define focused repository methods**

Replace the read-modify-write lease path with methods equivalent to:

```csharp
Task<RouterNodeLease?> TryAcquireLeaseAsync(
    IReadOnlyCollection<string> candidateNodeIds,
    RouterLease lease,
    CancellationToken ct = default);

Task<bool> ReleaseLeaseAsync(string leaseId, CancellationToken ct = default);

Task<int> ReleaseExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default);

Task<bool> ApplyHealthAsync(
    string nodeId,
    RouterNodeStatus status,
    DateTimeOffset checkedAtUtc,
    int? latencyMs,
    string? errorCode,
    CancellationToken ct = default);
```

Do not expose `in_flight_requests` mutation as a generic upsert operation. On node conflict, retain the persisted in-flight value.

- [ ] **Step 2: Implement PostgreSQL acquisition as one transaction**

Open one Npgsql transaction. Select a candidate only from `@CandidateNodeIds`, require `enabled = true` and `status = 'Healthy'`, order by live `in_flight_requests::numeric / capacity_weight`, `in_flight_requests`, `name`, and use `FOR UPDATE SKIP LOCKED LIMIT 1`. In the same transaction increment the chosen node and insert `RouterLease`; return the chosen id, endpoint and control token. Commit only after both writes succeed.

Use a CTE shaped as:

```sql
with candidate as (
    select id, endpoint_url, control_token
    from control.router_nodes
    where id = any(@CandidateNodeIds)
      and enabled = true
      and status = 'Healthy'
    order by in_flight_requests::numeric / capacity_weight, in_flight_requests, name
    for update skip locked
    limit 1
), leased_node as (
    update control.router_nodes node
    set in_flight_requests = node.in_flight_requests + 1
    from candidate
    where node.id = candidate.id
    returning node.id, candidate.endpoint_url, candidate.control_token
)
select * from leased_node;
```

Insert the lease only when the CTE returns a node; return null otherwise.

- [ ] **Step 3: Implement atomic release and expiry cleanup**

Release one lease with a CTE that deletes it, decrements its node with `greatest(in_flight_requests - 1, 0)`, and returns whether a row existed. Release expired leases in one CTE that groups deleted `node_id` values and decrements each node by its exact count:

```sql
with expired as (
    delete from control.router_leases
    where expires_at_utc <= @NowUtc
    returning node_id
), counts as (
    select node_id, count(*)::int as lease_count
    from expired
    group by node_id
)
update control.router_nodes node
set in_flight_requests = greatest(node.in_flight_requests - counts.lease_count, 0)
from counts
where node.id = counts.node_id;
```

Implement the same count-based behavior under the in-memory repository's synchronization boundary.

- [ ] **Step 4: Add repository-level tests first**

Extend `ControlPlaneRepositoryPostgresTests.cs` with tests that concurrently acquire leases from one/two healthy nodes and verify the stored in-flight total equals the number of leases. Add a test that expires multiple leases for one node and verifies the total is decremented by the exact count. Run the targeted tests before wiring the service.

### Task 2: Use an immutable metadata snapshot on the selection hot path

**Files:**
- Create: `src/OpenRussiaRouter.ControlPlane/Application/Services/RouterNodeSnapshotStore.cs`
- Create: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Workers/RouterNodeSnapshotRefresher.cs`
- Create: `src/OpenRussiaRouter.ControlPlane/Application/Options/RouterNodeSnapshotOptions.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Application/Extensions/ControlPlaneExtensions.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/appsettings.json`

- [ ] **Step 1: Model only immutable routing metadata in the snapshot**

Store id, name, endpoint URL, control token, capacity weight, enabled and status. Do not store `in_flight_requests`. Implement atomic whole-snapshot replacement and a read method returning the current immutable collection.

- [ ] **Step 2: Refresh the snapshot in a hosted worker**

Every 10 seconds by default, read the complete metadata list through `ListNodesAsync` (following pagination if needed) and replace the store only after a successful complete refresh. Register validated options, the singleton snapshot store and the hosted worker. Keep the existing capability store independent.

- [ ] **Step 3: Cover worker behavior**

Add unit tests showing a successful run replaces the snapshot and a failed repository read preserves the last good snapshot.

### Task 3: Refactor the service to delegate all mutable lease and health operations

**Files:**
- Modify: `src/OpenRussiaRouter.ControlPlane/Application/Services/RouterControlPlaneService.cs`
- Modify: `tests/OpenRussiaRouter.ControlPlane.Tests/Application/RouterControlPlaneServiceTests.cs`
- Modify: `tests/OpenRussiaRouter.ControlPlane.Tests/Application/RouterLeaseCleanupWorkerTests.cs`
- Modify: `tests/OpenRussiaRouter.ControlPlane.Tests/Application/RouterHealthCheckWorkerTests.cs`

- [ ] **Step 1: Select candidate IDs from the snapshot**

Preserve capability, excluded-node and preferred-node-token filtering against stable metadata. When the metadata snapshot is empty at startup, make one existing repository list fallback so routing remains available. Pass only matching candidate IDs to `TryAcquireLeaseAsync`; never order on a cached in-flight value.

- [ ] **Step 2: Replace service read-modify-write paths**

Use `TryAcquireLeaseAsync` for selection, `ReleaseLeaseAsync` for normal release, `ReleaseExpiredLeasesAsync` for cleanup and the focused repository health update for `ApplyHealthAsync`. Remove the old `Lease()/UpsertNodeAsync`, `RemoveLeaseAsync/GetNodeAsync/Release()/UpsertNodeAsync`, and `RemoveExpiredLeasesAsync/ReleaseNodesAsync` service sequences.

- [ ] **Step 3: Preserve domain invariants**

For a missing requested node keep existing `KeyNotFoundException` behavior. For disabled nodes, health persistence must retain `Disabled` rather than revive them. Preserve preferred token matching, capability fallback when the capability store is empty, and existing error messages for no available router node.

- [ ] **Step 4: Add regression tests**

Add unit tests for exact decrement after multiple expired leases, a disabled node not being revived by health, and an expired cleanup operation that does not require the request cancellation token. Add PostgreSQL tests for concurrent selection and health updates, asserting no lost in-flight increments and no resurrected disabled status.

### Task 4: Verify, document, and close the backlog entries

**Files:**
- Modify: `src/OpenRussiaRouter.ControlPlane/CLAUDE.md`
- Modify: `.opencode/tasks.md`

- [ ] **Step 1: Update ControlPlane documentation**

Document that routing uses a stable metadata snapshot, while load remains database-authoritative; describe atomic acquire/release/expiry cleanup and the rule that health writes cannot overwrite lease state.

- [ ] **Step 2: Run focused and full verification**

Run:

```powershell
dotnet test tests/OpenRussiaRouter.ControlPlane.Tests --no-restore -nr:false -m:1
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: both commands exit 0 with no warnings. If Testcontainers cannot start, report the PostgreSQL-test skip explicitly.

- [ ] **Step 3: Mark and commit**

Set T-007, T-008 and T-018 to `done` with actual ISO-8601 timestamps, then run:

```powershell
git add -- src/OpenRussiaRouter.ControlPlane tests/OpenRussiaRouter.ControlPlane.Tests .opencode/tasks.md
git commit -m "fix(control-plane): make router lease routing atomic"
```

