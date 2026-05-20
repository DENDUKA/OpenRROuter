# Node Provider Limits Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Provider Limits" action on `/admin/nodes` linking to a new page that displays per-provider quota progress bars for a 9Router node.

**Architecture:** New Razor Page `NodeProviderLimits` in WebApp calls the already-existing `ControlPlaneClient.GetRouterNodeDetailsAsync`, parses the `UsageJson` field on each provider entry into typed view models, and renders Bootstrap progress bars. No changes to gRPC/proto/ControlPlane service.

**Tech Stack:** ASP.NET Core Razor Pages, C# 12 records, `System.Text.Json`, Bootstrap 5 progress bars, Feather-style SVG icons.

---

## File Map

| Action | Path |
|--------|------|
| **Create** | `src/OpenRussiaRouter.WebApp/Pages/Admin/NodeProviderLimits.cshtml.cs` |
| **Create** | `src/OpenRussiaRouter.WebApp/Pages/Admin/NodeProviderLimits.cshtml` |
| **Modify** | `src/OpenRussiaRouter.WebApp/Pages/Admin/Nodes.cshtml` — add icon link in Actions column |

---

### Task 1: Page model — NodeProviderLimits.cshtml.cs

**Files:**
- Create: `src/OpenRussiaRouter.WebApp/Pages/Admin/NodeProviderLimits.cshtml.cs`

- [ ] **Step 1: Create the file with full implementation**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodeProviderLimitsModel(
    ControlPlaneClient controlPlaneClient,
    ILogger<NodeProviderLimitsModel> logger) : PageModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string NodeId { get; private set; } = string.Empty;
    public string NodeName { get; private set; } = string.Empty;
    public IReadOnlyList<ProviderLimitView> Providers { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(string nodeId)
    {
        NodeId = nodeId;
        try
        {
            var details = await controlPlaneClient.GetRouterNodeDetailsAsync(nodeId);
            NodeName = details.Node.Name;
            Providers = details.Providers.Select(ParseProvider).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load provider limits. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to load provider limits.";
        }
    }

    public static string GetProgressBarClass(double remainingPercentage) => remainingPercentage switch
    {
        > 50 => "bg-success",
        > 20 => "bg-warning",
        _ => "bg-danger"
    };

    public static string GetStatusBadge(bool isActive) =>
        isActive ? "badge-success" : "badge-secondary";

    private static ProviderLimitView ParseProvider(RouterProviderQuotaInfo p)
    {
        var quotas = ParseQuotas(p.UsageJson);
        return new ProviderLimitView(p.ConnectionId, p.Provider, p.Name, p.IsActive,
            p.QuotaSupported, p.Error, quotas);
    }

    private static IReadOnlyList<QuotaView> ParseQuotas(string? usageJson)
    {
        if (string.IsNullOrWhiteSpace(usageJson))
            return [];
        try
        {
            var usage = JsonSerializer.Deserialize<ProviderUsageDto>(usageJson, JsonOptions);
            if (usage?.Quotas is not { Count: > 0 })
                return [];

            return usage.Quotas
                .Select(kvp => new QuotaView(
                    kvp.Key,
                    kvp.Value.RemainingPercentage,
                    kvp.Value.ResetAt,
                    kvp.Value.Unlimited))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record ProviderUsageDto
    {
        [JsonPropertyName("quotas")]
        public Dictionary<string, QuotaEntryDto>? Quotas { get; init; }
    }

    private sealed record QuotaEntryDto
    {
        [JsonPropertyName("remainingPercentage")]
        public double RemainingPercentage { get; init; }

        [JsonPropertyName("resetAt")]
        public string? ResetAt { get; init; }

        [JsonPropertyName("unlimited")]
        public bool Unlimited { get; init; }
    }
}

public sealed record ProviderLimitView(
    string ConnectionId,
    string Provider,
    string Name,
    bool IsActive,
    bool QuotaSupported,
    string? Error,
    IReadOnlyList<QuotaView> Quotas);

public sealed record QuotaView(
    string Label,
    double RemainingPercentage,
    string? ResetAt,
    bool Unlimited);
```

- [ ] **Step 2: Build to verify no compile errors**

```powershell
dotnet build src/OpenRussiaRouter.WebApp --no-restore -nr:false -m:1
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add src/OpenRussiaRouter.WebApp/Pages/Admin/NodeProviderLimits.cshtml.cs
git commit -m "feat: add NodeProviderLimits page model"
```

---

### Task 2: Razor view — NodeProviderLimits.cshtml

**Files:**
- Create: `src/OpenRussiaRouter.WebApp/Pages/Admin/NodeProviderLimits.cshtml`

- [ ] **Step 1: Create the view**

```cshtml
@page "/admin/nodes/{nodeId}/provider-limits"
@model Admin.NodeProviderLimitsModel
@{ Layout = "~/Pages/Shared/_AdminLayout.cshtml"; ViewData["Title"] = "Provider Limits - Admin"; }

<div class="adm-section-head">
    <h2>Provider Limits — @Model.NodeName</h2>
    <p>Remaining quota per provider connection on this node.</p>
</div>

<div class="adm-btn-row">
    <a class="btn btn-secondary" href="/admin/nodes/@Model.NodeId">Back to node</a>
</div>

@if (Model.ErrorMessage != null)
{
    <div class="alert alert-danger" role="alert">@Model.ErrorMessage</div>
}

@if (Model.Providers.Count == 0 && Model.ErrorMessage == null)
{
    <div class="adm-card">
        <div class="adm-card-body">
            <p class="text-muted">No provider data available for this node.</p>
        </div>
    </div>
}

@foreach (var provider in Model.Providers)
{
    <div class="adm-card" data-testid="provider-limits-card" data-connection-id="@provider.ConnectionId">
        <div class="adm-card-header adm-card-header--split">
            <span>
                @provider.Name
                <small class="text-muted">(@provider.Provider)</small>
            </span>
            <span class="badge @Admin.NodeProviderLimitsModel.GetStatusBadge(provider.IsActive)">
                @(provider.IsActive ? "active" : "inactive")
            </span>
        </div>
        <div class="adm-card-body">
            @if (provider.Error != null)
            {
                <span class="text-danger" data-testid="provider-error">@provider.Error</span>
            }
            else if (!provider.QuotaSupported)
            {
                <span class="text-muted" data-testid="quota-not-supported">Quota not supported</span>
            }
            else if (provider.Quotas.Count == 0)
            {
                <span class="text-muted" data-testid="no-quota-data">No quota data</span>
            }
            else
            {
                <table class="table table-sm" style="margin-bottom:0">
                    <tbody>
                        @foreach (var quota in provider.Quotas)
                        {
                            <tr data-testid="quota-row">
                                <td style="width:200px;vertical-align:middle">
                                    <span>@quota.Label</span>
                                </td>
                                <td style="vertical-align:middle">
                                    @if (quota.Unlimited)
                                    {
                                        <span class="text-muted" data-testid="quota-unlimited">Unlimited</span>
                                    }
                                    else
                                    {
                                        var pct = Math.Round(quota.RemainingPercentage, 1);
                                        var barClass = Admin.NodeProviderLimitsModel.GetProgressBarClass(quota.RemainingPercentage);
                                        <div class="progress" style="height:16px;min-width:160px" data-testid="quota-bar">
                                            <div class="progress-bar @barClass"
                                                 role="progressbar"
                                                 style="width:@(pct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))%"
                                                 aria-valuenow="@pct"
                                                 aria-valuemin="0"
                                                 aria-valuemax="100">
                                                @pct%
                                            </div>
                                        </div>
                                    }
                                </td>
                                <td style="vertical-align:middle;white-space:nowrap">
                                    @if (!quota.Unlimited && quota.ResetAt != null)
                                    {
                                        var resetTime = DateTimeOffset.TryParse(quota.ResetAt, out var dt)
                                            ? dt.ToLocalTime().ToString("g")
                                            : quota.ResetAt;
                                        <small class="text-muted" data-testid="quota-reset">Resets: @resetTime</small>
                                    }
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            }
        </div>
    </div>
}
```

- [ ] **Step 2: Build to verify no compile errors**

```powershell
dotnet build src/OpenRussiaRouter.WebApp --no-restore -nr:false -m:1
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add src/OpenRussiaRouter.WebApp/Pages/Admin/NodeProviderLimits.cshtml
git commit -m "feat: add NodeProviderLimits razor view"
```

---

### Task 3: Add gauge icon link in Nodes.cshtml

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Admin/Nodes.cshtml`

The action column currently starts with the FetchModels form button. Add a bar-chart icon link **after** the FetchModels form and **before** the Enable/Disable form.

- [ ] **Step 1: Add the icon link**

In `src/OpenRussiaRouter.WebApp/Pages/Admin/Nodes.cshtml`, find this block (around line 87):

```cshtml
                            </form>
                                @if (node.Status == "disabled")
```

Replace with:

```cshtml
                            </form>
                                <a href="/admin/nodes/@node.NodeId/provider-limits"
                                   class="btn-icon"
                                   title="Provider limits"
                                   aria-label="Provider limits"
                                   data-testid="btn-provider-limits">
                                    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24"
                                         fill="none" stroke="currentColor" stroke-width="2"
                                         stroke-linecap="round" stroke-linejoin="round">
                                        <line x1="18" y1="20" x2="18" y2="10"/>
                                        <line x1="12" y1="20" x2="12" y2="4"/>
                                        <line x1="6" y1="20" x2="6" y2="14"/>
                                    </svg>
                                </a>
                                @if (node.Status == "disabled")
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build src/OpenRussiaRouter.WebApp --no-restore -nr:false -m:1
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add src/OpenRussiaRouter.WebApp/Pages/Admin/Nodes.cshtml
git commit -m "feat: add provider limits icon link to nodes list"
```

---

### Task 4: Final build verification

- [ ] **Step 1: Full solution build**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 2: Run tests**

```powershell
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```

Expected: all tests pass (no new tests needed — page model has no business logic beyond JSON parsing, which is covered by the build).

- [ ] **Step 3: Manual smoke test**

1. Run WebApp: `dotnet run --project src/OpenRussiaRouter.WebApp`
2. Navigate to `/admin/nodes`
3. Verify the bar-chart icon appears in the Actions column for each node
4. Click the icon → should navigate to `/admin/nodes/{nodeId}/provider-limits`
5. Verify provider cards appear with progress bars (or "No quota data" / "Quota not supported" for providers without quota)
6. Verify bar colors: green >50%, yellow 20–50%, red <20%
7. Verify "Unlimited" text shows instead of a bar for unlimited quotas
