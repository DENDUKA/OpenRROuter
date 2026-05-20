# Node Provider Limits Page — Design Spec

**Date:** 2026-05-21  
**Status:** Approved

## Goal

Add a "Provider Limits" action on `/admin/nodes` that links to a new page `/admin/nodes/{nodeId}/provider-limits`. The page fetches provider quota data for all providers on a node and displays each quota as a progress bar showing remaining capacity (0–100%).

## Data Source

Uses the existing `ControlPlaneClient.GetRouterNodeDetailsAsync(nodeId)` — no new gRPC methods or proto changes needed.

The `UsageJson` field in each `RouterProviderQuotaInfo` contains JSON from 9Router's `GET /api/integrations/providers/quota` with this structure:

```json
{
  "plan": "Claude Pro",
  "quotas": {
    "session (5h)": {
      "used": 87,
      "total": 100,
      "remaining": 13,
      "remainingPercentage": 13,
      "resetAt": "2026-05-21T08:30:00Z",
      "unlimited": false
    }
  }
}
```

`remainingPercentage` is pre-computed by 9Router (0–100, higher = more remaining).

## New Files

- `src/OpenRussiaRouter.WebApp/Pages/Admin/NodeProviderLimits.cshtml`
- `src/OpenRussiaRouter.WebApp/Pages/Admin/NodeProviderLimits.cshtml.cs`

## Modified Files

- `src/OpenRussiaRouter.WebApp/Pages/Admin/Nodes.cshtml` — add gauge icon link in Actions column

## Page Model

**Route:** `/admin/nodes/{nodeId}/provider-limits`  
**Auth:** `[Authorize(Roles = "Admin")]`

On `OnGetAsync(nodeId)`:
1. Call `GetRouterNodeDetailsAsync(nodeId)`
2. Map each provider's `UsageJson` → `IReadOnlyList<QuotaView>` by deserializing the `quotas` dictionary

View models (private to page model):
```csharp
record ProviderLimitView(string ConnectionId, string Provider, string Name,
    bool IsActive, bool QuotaSupported, string? Error, IReadOnlyList<QuotaView> Quotas);

record QuotaView(string Label, double RemainingPercentage, string? ResetAt, bool Unlimited);
```

JSON parsing is best-effort; if `UsageJson` is null, malformed, or has no `quotas` key → show "No quota data".

## View Layout

- Header: "Provider Limits — {NodeName}" + Back link to `/admin/nodes/{nodeId}`
- One card per provider:
  - Header: `{Name}` (`{Provider}`) + active/inactive badge
  - If `Error != null` → red error text
  - If `QuotaSupported=false` → grey "Quota not supported"
  - If no quotas parsed → grey "No quota data"
  - Otherwise: one row per quota with label, progress bar, reset time

## Progress Bar Spec

- Width = `remainingPercentage`%
- Color:
  - `>50%` → `bg-success` (green)
  - `20–50%` → `bg-warning` (yellow)
  - `<20%` → `bg-danger` (red)
- `unlimited=true` → show "Unlimited" text, no bar
- Reset time shown as relative label: `Resets: {resetAt:g local time}`

## Action Button in Nodes.cshtml

Gauge SVG icon (`<a>` tag, not a form) linking to `/admin/nodes/{nodeId}/provider-limits`. Placed before the Enable/Disable buttons in the Actions column.
