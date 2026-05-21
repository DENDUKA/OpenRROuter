# Node Models Dropdown on Admin Models Form

**Date:** 2026-05-21

## Problem

The Admin/Models form creates/edits billing model records. The `upstream_model_id` (the real model ID sent to the 9Router node) is currently set automatically to `Form.Id` — there is no UI to select from models actually available on connected nodes. Admins must know and type the correct 9Router model ID manually.

## Goal

Add a dropdown to the create/edit model form that lists real model IDs from connected 9Router nodes. Selecting one sets `upstream_model_id` for the billing record.

## Architecture

```
WebApp Admin/Models (GET)
    → ControlPlane gRPC: ListAvailableNodeModels
        → for each enabled node: GET {node.EndpointUrl}/v1/models
                                  Authorization: Bearer {control_token}
        → deduplicate by model.id (HashSet)
    ← string[] model_ids (sorted)
    ← dropdown populated in form

WebApp Admin/Models (POST Save)
    → BillingService gRPC: UpsertModelPrice
        upstream_model_id = Form.RouterModelId (selected from dropdown)
```

## Changes

### 1. `src/OpenRussiaRouter.Contracts/Protos/control-plane.proto`

Add new RPC and messages:

```protobuf
rpc ListAvailableNodeModels (ListAvailableNodeModelsRequest)
    returns (ListAvailableNodeModelsResponse);

message ListAvailableNodeModelsRequest {}

message ListAvailableNodeModelsResponse {
  repeated string model_ids = 1;
}
```

### 2. `src/OpenRussiaRouter.ControlPlane/Infrastructure/RouterNodeProviderClient.cs`

Add method:

```csharp
Task<IReadOnlyList<string>> ListModelsAsync(RouterNode node, CancellationToken ct);
```

Implementation: `GET {node.EndpointUrl}/v1/models` with `Authorization: Bearer {control_token}`. Deserializes `{ data: [{ id: string }] }`. Returns list of model IDs. On HTTP failure returns empty list (caller skips the node).

### 3. `src/OpenRussiaRouter.ControlPlane/Application/Services/GrpcControlPlaneService.cs`

Add handler for `ListAvailableNodeModels`:

1. Load all `enabled = true` nodes from repository
2. For each node call `RouterNodeProviderClient.ListModelsAsync(node, ct)` in parallel
3. Collect all IDs into `HashSet<string>` (deduplication)
4. Return sorted list

### 4. `src/OpenRussiaRouter.WebApp/Services/ControlPlane/ControlPlaneClient.cs`

Add method:

```csharp
Task<IReadOnlyList<string>> ListAvailableNodeModelsAsync(CancellationToken ct = default);
```

Calls the new gRPC `ListAvailableNodeModels` RPC. Returns empty list on failure.

### 5. `src/OpenRussiaRouter.WebApp/Pages/Admin/Models.cshtml.cs`

- Add property: `public IReadOnlyList<string> AvailableNodeModels { get; set; } = [];`
- Add form field: `public string RouterModelId { get; set; } = string.Empty;`
- In `OnGetAsync`: populate `AvailableNodeModels` via `ControlPlaneClient.ListAvailableNodeModelsAsync()`
- In `OnPostSaveAsync`: use `Form.RouterModelId` as `upstream_model_id` (replaces `orig?.UpstreamModelId ?? Form.Id`)
- On edit: pre-populate `Form.RouterModelId` from existing `model.UpstreamModelId`

### 6. `src/OpenRussiaRouter.WebApp/Pages/Admin/Models.cshtml`

Replace the implicit upstream_model_id with an explicit dropdown field:

- If `AvailableNodeModels` is non-empty: render `<select>` with options from the list. Pre-select current value on edit.
- If `AvailableNodeModels` is empty (all nodes unreachable): render `<input type="text">` fallback with a warning message.
- Label: "Router Model" or "Upstream Model"

## Error Handling

| Scenario | Behavior |
|---|---|
| Node HTTP call fails | Skip node, continue with others |
| All nodes unreachable | `AvailableNodeModels` is empty, fallback to text input |
| Node returns malformed JSON | Skip node |
| Selected model not in list on edit | Pre-populate value as first `<option>` so it is preserved on save |

## What Does NOT Change

- `model_id` (the billing key exposed to API clients) remains free-text — set by admin
- `display_name`, pricing fields — unchanged
- `BillingService` gRPC interface — `UpsertModelPrice` already has `upstream_model_id`, no proto change needed there
- No caching — list is always fetched fresh when the form loads
