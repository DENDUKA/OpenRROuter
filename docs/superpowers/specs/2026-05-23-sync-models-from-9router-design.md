# Sync Models from 9Router

**Date:** 2026-05-23

## Problem

Admins add billing models one-by-one via Admin → Models. The upstream-model dropdown only returns `id` from `/v1/models`, so admins still type Display Name manually and have no way to discover which models 9Router actually exposes vs. what's already configured. With 9RFork advertising 20+ models per node, this is slow and error-prone.

Additionally, the user-facing `/models` page guesses provider/capability/context from string heuristics on `model.id` — there is no real metadata behind the cards.

## Goal

1. **Admin batch import**: a "Sync from 9Router" view that shows a diff (new on node / missing on node / in sync), lets admin select multiple models, set per-row pricing and enabled state, and upsert them in one POST.
2. **Enrichment**: pre-fill `display_name`, `type` (text/vision/embedding), and source-provider info from the richer `/api/integrations/providers/by-type/{provider}/models` endpoint instead of the plain `/v1/models`.

**Acceptance for the core feature (SYNC-1..6):** Goals 1 and 2 above. SYNC-0 discovery is already resolved in this spec. The user-facing `/models` page rewrite is explicitly *out of scope* — it lives in the Future section as SYNC-7 and may ship independently.

## Non-goals

- Auto-setting prices from external catalogs (OpenRouter/HuggingFace). Prices stay admin-curated; the Sync UI provides defaults the admin must confirm.
- Versioned price history (BILLING-003) — unchanged.
- Auto-sync on a schedule. This is a manual admin action; cron can come later.

## Architecture

```
WebApp Admin/Models/Sync (GET)
    → ControlPlane gRPC: ListNodeModelCatalog
        → for each enabled node:
            1. GET {node}/v1/models                                   (id list)
            2. (optional) GET {node}/api/providers                    (provider connections)
            3. for each provider:
                 GET {node}/api/integrations/providers/by-type/{p}/models
                                                                       (id, name, type, sources)
        → merge by upstream_id, attach which nodes serve it
    ← UpstreamModelCatalogEntry[]
    ← Billing.ListModels → diff: NewOnNodes / Configured / MissingFromNodes
    ← render selectable table

WebApp Admin/Models/Sync (POST Import)
    → for each Form.Rows[i] where Selected == true:
        BillingService gRPC: UpsertModelPrice
            model_id           = (prefix ?? "") + entry.public_id
            display_name       = entry.display_name           (re-derived server-side)
            upstream_model_id  = entry.upstream_id            (re-derived server-side)
            *_price_per_1m_rub = row.InputPrice / OutputPrice / CachePrice (per-row)
            enabled            = row.Enabled                  (per-row)
    → redirect to /admin/models with success count
```

## Changes

### 1. `src/OpenRussiaRouter.Contracts/Protos/control-plane.proto`

Add richer catalog RPC alongside existing `ListAvailableNodeModels`:

```protobuf
rpc ListNodeModelCatalog (ListNodeModelCatalogRequest)
    returns (ListNodeModelCatalogResponse);

message ListNodeModelCatalogRequest {}

message UpstreamModelEntry {
  string upstream_id   = 1;  // id sent to 9Router (e.g. "MiniMax-M2.7")
  string public_id     = 2;  // suggested billing model_id from 9Router's PublicId
                             // (e.g. "minimax/MiniMax-M2.7"); falls back to upstream_id
                             // when 9Router doesn't expose a PublicId for the entry
  string display_name  = 3;
  string model_type    = 4;  // text | vision | embedding | …
  repeated string source_providers = 5;  // openrouter, anthropic, …
  repeated string node_ids = 6;          // which nodes serve this model
}

message ListNodeModelCatalogResponse {
  repeated UpstreamModelEntry entries = 1;
  repeated string unreachable_node_ids = 2;
}
```

Keep `ListAvailableNodeModels` — it's used by the existing single-add form and is cheaper.

### 2. `src/OpenRussiaRouter.ControlPlane/Infrastructure/RouterNodeProviderClient.cs`

Add provider-discovery helper if missing:

```csharp
Task<RouterProviderListResponse> ListProvidersAsync(RouterNode node, CancellationToken ct);
```

Endpoint: `GET /api/providers`. `ListProviderModelsAsync` already exists.

**SYNC-0 discovery result:** current 9RFork does **not** expose `GET /api/integrations/providers`. The existing provider-discovery endpoint is `GET /api/providers`, implemented in `9RFork/src/app/api/providers/route.js`. It returns safe provider connection metadata with secrets removed (`apiKey`, `accessToken`, `refreshToken`, `idToken` are set to `undefined` before serialization).

```json
{
  "connections": [
    {
      "id": "connection-id",
      "provider": "minimax",
      "authType": "apikey",
      "name": "MiniMax account",
      "isActive": true,
      "testStatus": "active",
      "providerSpecificData": {
        "prefix": "minimax"
      }
    },
    {
      "id": "compatible-connection-id",
      "provider": "openai-compatible-uuid",
      "authType": "apikey",
      "name": "Local OpenAI-compatible node",
      "isActive": true,
      "providerSpecificData": {
        "prefix": "myllm",
        "baseUrl": "http://localhost:1234/v1",
        "nodeName": "Local LLM"
      }
    }
  ]
}
```

Define `RouterProviderListResponse` against this shape:

```csharp
public sealed record RouterProviderListResponse
{
    public IReadOnlyList<RouterProviderConnectionSummary> Connections { get; init; } = [];
}

public sealed record RouterProviderConnectionSummary
{
    public string Id { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string AuthType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string? TestStatus { get; init; }
    public RouterProviderSpecificDataSummary? ProviderSpecificData { get; init; }
}

public sealed record RouterProviderSpecificDataSummary
{
    public string? Prefix { get; init; }
    public string? BaseUrl { get; init; }
    public string? NodeName { get; init; }
}
```

The sync flow only needs `Provider`, `IsActive`, and optionally `ProviderSpecificData.Prefix` for display/debug context. Use `connection.Provider` as the provider key passed to `/api/integrations/providers/by-type/{provider}/models?includeCompatible=true`; for compatible nodes this is the node id (for example `openai-compatible-uuid`), which the by-type endpoint can resolve when `includeCompatible=true`.

### 3. `src/OpenRussiaRouter.ControlPlane/Application/Services/GrpcRouterControlPlaneService.cs`

Add handler `ListNodeModelCatalog`:

1. Load all enabled nodes from repository
2. For each node, in parallel:
   - Try `ListProvidersAsync(node)` (`GET /api/providers`).
     - **404 / NotFound** → older 9Router without provider connections endpoint. Fall back: call `/v1/models` for this node and emit minimal entries (`upstream_id` only, `display_name = ""`, `model_type = ""`, `source_providers = []`). Mark each entry as `IsFallback = true` internally so the merge step can prefer richer data from other nodes. Do **not** mark node as unreachable.
     - **Other HTTP error / timeout / network exception** → add `node_id` to `unreachable_node_ids`, skip the node entirely.
   - From `response.Connections`, **filter to `IsActive == true` only**. Inactive provider connections must not be used for fan-out.
   - Deduplicate the remaining active connections by `Provider`; multiple active accounts for the same provider should produce one `ListProviderModelsAsync` call because the by-type endpoint aggregates active connections internally.
   - For each remaining active provider key, call `ListProviderModelsAsync(node, providerKey, source: null, includeCompatible: true)`.
     - **404 on a specific provider** → skip that provider, keep others (provider may have been deleted between calls).
     - **Other HTTP error** → skip this provider, log warning; do *not* mark whole node unreachable since other providers may have succeeded.
3. Merge across nodes by `upstream_id` — **rank-based, not first-seen**. Each per-node observation carries a source rank:
   - Rank 2 (richest): provider catalog `/api/integrations/providers/by-type/{p}/models` — has `Id`, `PublicId`, `Name`, `Type`, sources
   - Rank 1 (fallback): `/v1/models` — only `id`. Entries from this path are tagged `IsFallback = true` and populate `upstream_id`; `public_id` is set to `upstream_id` purely as a placeholder, `display_name`/`model_type` stay empty.

   Merge per scalar field (`public_id`, `display_name`, `model_type`):
   - If incoming rank > current rank → incoming overwrites, **even if current is non-empty**. This is the critical case: a fallback entry processed first must not pin `public_id = upstream_id` and block a later provider-catalog entry from delivering the real namespaced id (e.g. `minimax/MiniMax-M2.7`).
   - If incoming rank == current rank → if current is empty and incoming is non-empty, overwrite; otherwise keep first. Within the same rank, ties are broken by arrival order.
   - If incoming rank < current rank → ignore the field (richer source already won).

   Other rules:
   - `public_id` value selection within rank 2: `providerModel.PublicId` if non-empty, else `upstream_id`. So a rank-2 entry with empty `PublicId` can still be overridden by a later rank-2 entry that *has* one (treat empty `PublicId` as "field absent" for the merge — same as empty `display_name`).
   - `node_ids`, `source_providers` accumulated and deduped across all sources (including rank-1 fallback).
   - Admin can still override at import time via the prefix field, but a non-empty `public_id` from 9Router is the default — not `upstream_id`.
   - Final entries do not expose rank/`IsFallback` — internal merge hints only.
4. Sort by `upstream_id`, return

In short: `unreachable_node_ids` means **0 useful data from the node**. A 404 on the provider connections endpoint is a known older-version case and is handled silently, not flagged as unreachable.

### 4. `src/OpenRussiaRouter.WebApp/Services/ControlPlane/ControlPlaneClient.cs`

Add:

```csharp
Task<NodeModelCatalogResult> ListNodeModelCatalogAsync(CancellationToken ct = default);
```

Returns mapped DTO `NodeModelCatalogResult { Entries, UnreachableNodeIds, Error }`. On gRPC failure → return empty entries + non-null `Error` string. Callers must check `Error` separately from `Entries.Count == 0` — empty catalog with `Error == null` is a valid "no models on any node" state, whereas a non-null `Error` means the call itself failed and downstream logic cannot trust the empty result.

### 5. New page: `src/OpenRussiaRouter.WebApp/Pages/Admin/ModelsSync.cshtml(.cs)`

Routed at `/admin/models/sync`. Layout: `_AdminLayout`.

**GET**

- Load catalog via `ControlPlaneClient.ListNodeModelCatalogAsync()`. If the result has a non-null `Error`, render the page in "catalog unavailable" mode: hide the import form, show the error banner, render existing billing-only as read-only context.
- Load existing billing models via `BillingClient.ListModelsAsync()`
- Build two indexes from existing billing: `byBillingId` (`model.Id → ModelInfo`) and `byUpstream` (`model.UpstreamModelId → ModelInfo`).
- Compute three sets:
  - `NewOnNodes` — catalog entries where `byUpstream` does NOT contain `entry.upstream_id` AND `byBillingId` does NOT contain `entry.public_id`
  - `Configured` — catalog entries where `byUpstream` contains `entry.upstream_id` (informational, default unchecked, no checkbox)
  - `MissingFromNodes` — billing models whose `UpstreamModelId` is not in the catalog AND not in `unreachable_node_ids` context (warning row; can't tell apart "node down" vs "model truly removed" if any node was unreachable, so suppress this list when `unreachable_node_ids` is non-empty)
- Render a table with one row per `NewOnNodes` catalog entry. Each row has its own form inputs:
  - `Selected` checkbox (whether to import this row)
  - `InputPrice` / `OutputPrice` / `CachePrice` (RUB/1M, per-row, validated `[0, 1_000_000]`)
  - `Enabled` checkbox (whether the imported model is enabled in billing)
  - `UpstreamId` (hidden — identifies which catalog entry this row maps to)
- Plus one form-wide field:
  - Optional `public_id` prefix (e.g. `myorg/`) — applied to every Suggested Billing ID
- Show `unreachable_node_ids` as a banner

**POST Import** — *trust model: server-authoritative for metadata, client-supplied for pricing.*

The form posts:

- `Form.Prefix` (string, optional)
- `Form.Rows[i].UpstreamId` (string — identifies the catalog entry)
- `Form.Rows[i].Selected` (bool)
- `Form.Rows[i].InputPrice` / `OutputPrice` / `CachePrice` (decimal, validated)
- `Form.Rows[i].Enabled` (bool)

It does **not** post `display_name`, `public_id`, `model_type`, or any other per-entry metadata as hidden inputs. Metadata is always re-derived server-side from the live catalog.

Handler:

1. Re-fetch catalog via `ListNodeModelCatalogAsync()` (do not trust browser snapshot).
   - **If `result.Error` is non-null → abort the import entirely.** Do not enter the per-row loop. Render the Sync page again (no redirect) with a top-level banner: "Catalog unavailable — import cancelled: {Error}". Submitted `Form.Rows` (with prices, selections, enabled state) are preserved so the admin can retry once nodes recover. This prevents the "model no longer on nodes" warning from being emitted for every single row on a transient gRPC failure.
2. Re-fetch existing billing via `ListModelsAsync()`; rebuild both `byBillingId` and `byUpstream` indexes. If this call fails → same abort behavior, banner: "Billing catalog unavailable — import cancelled."
3. For each `Form.Rows[i]` where `Selected == true`:
   - Look up the catalog entry by `UpstreamId`; if missing → skip with "model no longer on nodes" warning (now genuinely meaningful, since step 1 confirmed catalog was successfully refreshed)
   - Compute candidate `model_id = (prefix ?? "") + entry.public_id`
   - **Dedup check (both directions):**
     - If `byUpstream` already contains `entry.upstream_id` → skip with "already configured under '{existing.Id}'" warning (prevents importing a second public_id for the same upstream)
     - If `byBillingId` already contains `model_id` → skip with "billing ID '{model_id}' already used" warning (prevents overwriting an unrelated model)
   - Build `ModelInfo` from `entry.display_name` and `entry.upstream_id` (from re-fetched catalog), candidate `model_id`, `row.InputPrice`/`OutputPrice`/`CachePrice`, `enabled = row.Enabled`
   - Call `BillingClient.UpsertModelAsync(modelInfo)`
4. Count successes + collect per-row warnings/errors
5. Redirect to `/admin/models` with `TempData["SuccessMessage"] = "Imported N models."` and `TempData["WarningMessages"]` for the rest

The browser submits intent (which rows to import + per-row pricing/enabled state); the server independently derives the catalog metadata (`display_name`, `public_id`, `upstream_id`). No client-supplied metadata field is treated as the source of truth.

### 6. `src/OpenRussiaRouter.WebApp/Pages/Admin/Models.cshtml`

Add "Sync from 9Router" button next to "Add Model":

```html
<a class="btn btn-sm btn-secondary" href="/admin/models/sync">Sync from 9Router</a>
<a class="btn btn-sm btn-primary" href="?action=add">Add Model</a>
```

### 7. User-facing `/models` enrichment (optional follow-up)

Extend `BillingService.ListPublicModels` proto + handler to include `upstream_model_id`, `input_price`, `output_price`. Then in `Pages/Models.cshtml.cs`:
- Replace `GetProvider`/`GetCapability` heuristics with lookup against a cached `ListNodeModelCatalog` snapshot (TTL ~5 min, in-memory)
- Render real prices instead of "Route" placeholder

Mark this as a separate task — it doesn't block the admin Sync feature.

## Error Handling

| Scenario | Behavior |
|---|---|
| Node connection timeout / DNS / TLS / non-404 5xx on `/api/providers` | Node added to `unreachable_node_ids`, skipped from catalog. UI shows banner. |
| `/api/providers` returns 404 (older 9Router without provider connections endpoint) | Fall back to `/v1/models` for this node, emit minimal entries (id only). Node is NOT flagged unreachable. |
| Single provider's `/by-type/{p}/models` returns 404 | Skip that provider, keep others for the same node. |
| Single provider's `/by-type/{p}/models` returns other error | Log warning, skip that provider, keep others. Node NOT flagged unreachable. |
| No enabled nodes | Catalog empty (no error), UI shows "Add a node first" |
| gRPC `ListNodeModelCatalog` itself fails (timeout, control-plane down) | `result.Error` non-null. GET → "catalog unavailable" mode. POST → abort with banner, preserve selection |
| `ListModelsAsync` (billing) fails on POST | Same as catalog failure: abort import, banner, preserve selection |
| At least one node unreachable but catalog otherwise succeeded | `MissingFromNodes` list suppressed (can't distinguish "removed" from "node down") |
| Selected upstream already mapped in billing | Skip with "already configured under '{existing.Id}'" warning |
| Candidate `model_id` collides with existing billing row | Skip with "billing ID already used" warning |
| Billing upsert RPC fails for one entry | Count failure, continue; report per-row errors |

## Task breakdown

Numbered for the backlog. Each is independently mergeable.

| # | Task | Est. | Touches |
|---|---|---|---|
| SYNC-0 | **Done:** source-read 9RFork provider discovery contract. Current route is `GET /api/providers` with `{ connections: [...] }`; `GET /api/integrations/providers` does not exist. | 0d | docs/ |
| SYNC-1 | Add `ListProvidersAsync` to `RouterNodeProviderClient` using the `GET /api/providers` contract above | 0.5d | ControlPlane.Infrastructure |
| SYNC-2 | Add `ListNodeModelCatalog` RPC: proto + ControlPlane handler + merge logic + unit tests with in-memory provider-client stub | 1d | Contracts.Protos, ControlPlane.Application |
| SYNC-3 | Add `ControlPlaneClient.ListNodeModelCatalogAsync` to WebApp + DTOs | 0.25d | WebApp.Services.ControlPlane |
| SYNC-4 | New page `Admin/ModelsSync.cshtml(.cs)` with diff table, bulk defaults, Import handler | 1d | WebApp.Pages.Admin |
| SYNC-5 | Add "Sync from 9Router" entry-point on Admin/Models page | 0.1d | WebApp.Pages.Admin |
| SYNC-6 | E2E (Playwright): admin opens Sync, imports 2 models, checks they appear in catalog | 0.5d | OpenRussiaRouter.E2ETests |
| SYNC-7 *(opt)* | Enrich `ListPublicModels` proto with prices + upstream_id; rewrite `/models` UI | 1d | Contracts.Protos, BillingService, WebApp.Pages |

Total core (SYNC-1..6): ~3.35d. SYNC-0 is already resolved in this spec. SYNC-7 is the user-facing enrichment and can ship later.

## What does NOT change

- `BillingService.UpsertModelPrice` contract — already supports everything needed
- `Auth` / `ApiGateway` — untouched
- Database schemas — no migrations
- Existing single-add form on `Admin/Models` — kept as the precision tool; Sync is for bulk
