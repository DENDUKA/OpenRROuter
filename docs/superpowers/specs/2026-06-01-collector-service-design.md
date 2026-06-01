# Collector Service — Design

**Date:** 2026-06-01
**Status:** Approved (design), pending implementation plan

## Problem

Periodic polling and in-memory caching of node/external data are scattered across the
codebase in ad-hoc form, with at least one case of duplicated polling of the same node
endpoints. We want a single dedicated service that polls external sources on a schedule,
caches results in-memory, and serves them to other services over gRPC.

### Inventory of existing periodic / caching work

| Data | Collected by (today) | Read by |
|---|---|---|
| Node health | `RouterHealthCheckWorker` (BackgroundService) | routing (hot-path) |
| Leases | `RouterLeaseCleanupWorker` (BackgroundService) | routing (hot-path) |
| Model capabilities | `RouterModelCapabilityJob` (Quartz) → `NodeModelCapabilityStore` | **routing (hot-path)** + UI catalog |
| Node model catalog | live gRPC calls + `CachedRouterNodeProvider` TTL cache | WebApp public + admin Models/Sync |
| Provider quota | `ProviderQuotaCollectorWorker` (BackgroundService) | OTel gauge `orr_provider_quota` |
| OpenRouter catalog | `OpenRouterCatalogClient.LoadCatalogAsync` (on-demand) | WebApp admin enrich |

Two distinct categories emerge:

1. **Hot-path / ControlPlane-owned state** — health checks and lease cleanup mutate
   ControlPlane's own routing state in the node-selection hot path. These are not a
   "collect external data and serve it" concern and stay in ControlPlane.
2. **Read-only collected data** — model capabilities, node model catalog, provider quota,
   OpenRouter catalog. Multiple consumers want these, and collection is currently
   duplicated (model capabilities are polled twice: once by the Quartz job for the routing
   filter store, once via live gRPC calls for the UI catalog — both hit the same node
   endpoints).

### Key constraint

Model-capability data, although collected read-only, is read **synchronously in the node-
selection hot path** of ControlPlane:

```csharp
// RouterControlPlaneService.cs:107-108
if (!string.IsNullOrWhiteSpace(modelId) && !capabilityStore.IsEmpty)
    candidates = all.Where(n => capabilityStore.NodeSupportsModel(n.Id, modelId));
```

(Capability-aware routing is in fact implemented; the CLAUDE.md note claiming it is "not
done" is stale and should be corrected.)

Therefore the collector cannot become a synchronous dependency on the routing path. The
collector polls and caches; ControlPlane keeps a thin local replica of the capability data,
refreshed in the background from the collector. Routing latency is unchanged.

## Scope

**In scope (read-only collection):** model capabilities, node model catalog, provider
quota, OpenRouter catalog.

**Out of scope (stays in ControlPlane):** node registry (source of truth), health checks,
lease cleanup, integration-key push/revoke.

## Architecture

New service **`OpenRussiaRouter.CollectorService`** — gRPC, port **5005** (5001–5004 are
taken). It is the single point of periodic external polling + in-memory caching, and serves
cached snapshots to other services over gRPC. It is a gRPC client of ControlPlane for the
node list.

### What moves / what stays

**Stays in ControlPlane:**
- Node registry (source of truth), health checks, lease cleanup, integration-key
  push/revoke.
- `NodeModelCapabilityStore` stays as the local read-replica for hot-path routing, but is
  now populated by a thin background refresher that pulls a snapshot from the collector
  (replacing the Quartz `RouterModelCapabilityJob`).

**Moves to CollectorService:**
- Node model polling logic (from `RouterModelCapabilityJob`).
- Node model catalog assembly (from `GrpcRouterControlPlaneService.ListNodeModelCatalog` /
  `GetNodeModels`).
- `ProviderQuotaCollectorWorker` + its OTel `Meter` / `orr_provider_quota` gauge.
- OpenRouter catalog fetch (`OpenRouterCatalogClient.LoadCatalogAsync`).
- Read-only methods of the node-admin HTTP client (`ListProvidersWithModels`,
  `ListModels`, `GetProviderQuota`).

**Removed as redundant:**
- `CachedRouterNodeProvider` / `RouterNodeProviderCacheStore` (TTL cache over live calls) —
  replaced by the collector's snapshot model. This eliminates the double-polling of node
  endpoints.

**Shared:** the node-admin HTTP client is used for both read-only polling (collector) and
writes — integration-key push/revoke (ControlPlane). The read-only methods move to the
collector; the write methods stay in ControlPlane. The low-level HTTP client is either
placed in `BuildingBlocks` for reuse or thinly duplicated — to be resolved in the
implementation plan.

**Matching logic:** `OpenRouterCatalogClient.FindMatch` (slug-matching) stays in WebApp;
the collector serves the raw cached catalog entries.

### Contract — new `collector.proto`

```proto
service CollectorService {
  rpc GetCapabilitiesSnapshot (...) returns (...);  // node_id -> [model_ids] + timestamp; for ControlPlane replica
  rpc ListNodeModelCatalog    (...) returns (...);  // rich catalog; for WebApp (moves from control-plane.proto)
  rpc GetNodeModels           (...) returns (...);  // single node's models; admin Nodes page
  rpc GetOpenRouterCatalog    (...) returns (...);  // cached entries; admin enrich
  rpc GetProviderQuota        (...) returns (...);  // optional; primary channel is the OTel gauge
}
```

`ListNodeModelCatalog` and `GetNodeModels` are **removed from `control-plane.proto`**; WebApp
calls the collector directly. Provider quota continues to be exposed as the OTel metric
`orr_provider_quota`, now exported by the collector (Prometheus scrapes the collector).

### Data flow

1. Collector startup → `ControlPlane.ListRouterNodes` → node list (endpoint, control_token,
   enabled). Node list refreshed every ~60s.
2. Poll cycle (configurable interval): for each enabled node — `providers/with-models`
   (fallback `/v1/models` on 404) + `providers/quota`. Results are assembled and **atomically
   swapped** into the in-memory snapshot.
3. OpenRouter catalog polled on a separate interval (~15–30 min).
4. ControlPlane: a background refresher pulls `GetCapabilitiesSnapshot` every ~K seconds and
   updates the local `NodeModelCapabilityStore`. Routing latency is unchanged.
5. WebApp reads catalog / node models / OpenRouter catalog directly from the collector. The
   short `IMemoryCache` on the public Models page may be retained as-is.

### Degradation & error handling

- Per-node poll failure → log + skip; the snapshot is **not cleared** (keep stale — matches
  current `RouterModelCapabilityJob` behavior).
- Collector unreachable at ControlPlane startup → `NodeModelCapabilityStore.IsEmpty` →
  routing runs without the capability filter (the existing fallback at
  `RouterControlPlaneService.cs:107`). Graceful degradation preserved.
- gRPC deadlines via the shared `GrpcDeadlineInterceptor` and
  `GrpcServices:CallDeadlineSeconds`.
- Snapshots carry a timestamp so consumers can detect staleness.

### Configuration

- **CollectorService:** `GrpcServices:ControlPlane`, poll intervals (node poll, node-list
  refresh, OpenRouter), per-request timeouts, OpenRouter base URL.
- **ControlPlane:** `GrpcServices:Collector` + replica refresh interval.
- **WebApp:** `GrpcServices:Collector`.

### Deployment

- New project `src/OpenRussiaRouter.CollectorService`, gRPC port 5005.
- Docker Compose service + kustomize base/overlay manifests + ArgoCD application.
- Add the service to the service table and update the stale capability-routing note in
  CLAUDE.md.

### Testing

- **Unit:** snapshot store (atomic swap, keep-stale), poll cycle against a fake node
  provider, OpenRouter match. Existing capability/catalog-mapping tests move to the new test
  project.
- **Integration:** collector gRPC returns snapshots; ControlPlane replica refresher updates
  `NodeModelCapabilityStore` from the collector.

## Decisions

- Architecture: separate Collector service, node list via gRPC `ListRouterNodes`,
  ControlPlane keeps a local capability replica (chosen over direct-DB access and over
  in-ControlPlane consolidation).
- Scope: read-only collection only; health and lease stay in ControlPlane.
- WebApp reads the model catalog directly from the collector;
  `ListNodeModelCatalog`/`GetNodeModels` are removed from `control-plane.proto`.
- Cache is in-memory only and rebuilt on restart (acceptable per requirements).

## Open items for the implementation plan

- Placement of the shared node-admin HTTP client (BuildingBlocks vs thin duplication).
- Exact `collector.proto` message shapes (reuse existing catalog message definitions where
  possible).
- Replica refresh interval `K` and poll-cycle defaults.
