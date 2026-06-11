# Model Quota Percent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Calculate and expose remaining quota percentage per provider and per model as Prometheus metrics.

**Architecture:** 
1. `NodeQuotaCollector` will parse `limit` from JSON usage response, calculate `Percent`, and store it in `QuotaMeasurement`.
2. `NodePollingWorker` will correlate `QuotaSnapshot` with `CollectorSnapshot` to populate model mappings (list of `ModelIds`) in `QuotaMeasurement`.
3. `ProviderQuotaMeter` will create two new observable gauges: `orr_provider_quota_percent` (labels: node_id, provider, connection_id) and `orr_model_quota_percent` (labels: node_id, model_id).

**Tech Stack:** .NET 8, OpenTelemetry.Metrics, System.Text.Json

---

### Task 1: Update QuotaMeasurement to store Percent and ModelIds

**Files:**
- Modify: `src/OpenRussiaRouter.CollectorService/Application/Quota/QuotaMeasurement.cs`

- [ ] **Step 1: Modify QuotaMeasurement record**

Add `Percent` (double?) and `ModelIds` (IReadOnlyList<string>) properties.

```csharp
namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record QuotaMeasurement(
    string NodeId, 
    string Provider, 
    string ConnectionId, 
    double Remaining, 
    double? Percent,
    IReadOnlyList<string> ModelIds);
```

- [ ] **Step 2: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Quota/QuotaMeasurement.cs
git commit -m "feat: add Percent and ModelIds to QuotaMeasurement"
```

---

### Task 2: Calculate Percent in NodeQuotaCollector

**Files:**
- Modify: `src/OpenRussiaRouter.CollectorService/Application/Quota/NodeQuotaCollector.cs`

- [ ] **Step 1: Extract Limit and Calculate Percent**

Update `CollectAsync` to look for `limit` or `total` in the usage JSON and calculate `Percent = Remaining / Limit * 100`. Include empty ModelIds array.

```csharp
// Inside NodeQuotaCollector.cs (in the foreach (var provider in response.Providers) loop)
            if (!usage.TryGetProperty("remaining", out var remainingElement))
                continue;
            if (remainingElement.ValueKind is not (System.Text.Json.JsonValueKind.Number))
                continue;

            var remaining = remainingElement.GetDouble();
            double? percent = null;

            if (usage.TryGetProperty("limit", out var limitElement) && limitElement.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                var limit = limitElement.GetDouble();
                if (limit > 0)
                {
                    percent = remaining / limit * 100.0;
                }
            }
            else if (usage.TryGetProperty("total", out var totalElement) && totalElement.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                var total = totalElement.GetDouble();
                if (total > 0)
                {
                    percent = remaining / total * 100.0;
                }
            }

            measurements.Add(new QuotaMeasurement(
                node.NodeId,
                string.IsNullOrWhiteSpace(provider.Provider) ? "unknown" : provider.Provider,
                string.IsNullOrWhiteSpace(provider.ConnectionId) ? "unknown" : provider.ConnectionId,
                remaining,
                percent,
                [])); // Empty array for ModelIds, will be populated in NodePollingWorker
```

- [ ] **Step 2: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Quota/NodeQuotaCollector.cs
git commit -m "feat: extract limit and calculate percent in NodeQuotaCollector"
```

---

### Task 3: Correlate Quotas with Models in NodePollingWorker

**Files:**
- Modify: `src/OpenRussiaRouter.CollectorService/Application/Collection/NodePollingWorker.cs`

- [ ] **Step 1: Populate ModelIds in QuotaMeasurement**

In `NodePollingWorker.RunOnceAsync`, right before saving the `QuotaSnapshot`, map `ModelIds` from `catalogResults` to `quotaResults` based on `ConnectionId`.
Notice: The `NodeCatalogCollector` builds `Observations` where we have `SourceProviders`. In OpenRussiaRouter architecture `SourceProviders` represents the connection IDs. 

Wait, looking closely at `NodeCatalogCollector`, it iterates over `providersWithModels.Connections`. The `Connection` has `Id`. The models under that connection belong to it. However, `ModelObservation` throws away `connection.Id`! It maps `model.Sources` (which might be upstream providers) but not the local `connection.Id`. 

Let's fix `NodeCatalogCollector` first so we can map connection IDs. Wait, no, we can do mapping right inside `NodePollingWorker` by modifying `NodeCatalogCollector` slightly, or better: just update `ModelObservation` to include `ConnectionId`.

Actually, `NodeQuotaCollector` gives `ConnectionId`. `NodeCatalogCollector` has `connection.Id`. Let's update `ModelObservation` and `NodeCatalogCollector` to track `ConnectionId`.

**Let's refine Task 3 to include updating `ModelObservation`**

- [ ] **Step 1.1: Add ConnectionId to ModelObservation**

Modify `src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogResult.cs`:
```csharp
public sealed class ModelObservation
{
    public string UpstreamId { get; init; } = string.Empty;
    public string PublicId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ModelType { get; init; } = string.Empty;
    public IReadOnlyList<string> SourceProviders { get; init; } = [];
    public int Rank { get; init; }
    public string ConnectionId { get; init; } = string.Empty; // ADD THIS
}
```

- [ ] **Step 1.2: Populate ConnectionId in NodeCatalogCollector**

Modify `src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogCollector.cs`:
```csharp
                observations.Add(new ModelObservation
                {
                    UpstreamId = model.Id,
                    PublicId = string.IsNullOrWhiteSpace(model.PublicId) ? string.Empty : model.PublicId,
                    DisplayName = model.Name ?? string.Empty,
                    ModelType = model.Type ?? string.Empty,
                    SourceProviders = model.Sources ?? [],
                    Rank = 2,
                    ConnectionId = connection.Id // ADD THIS
                });
```

- [ ] **Step 1.3: Map ModelIds in NodePollingWorker**

Modify `src/OpenRussiaRouter.CollectorService/Application/Collection/NodePollingWorker.cs`. Replace the line `quotaStore.Set(new QuotaSnapshot(quotaResults.SelectMany(m => m).ToList(), now));` with:

```csharp
        var flatMeasurements = quotaResults.SelectMany(m => m).ToList();
        var enrichedMeasurements = new List<QuotaMeasurement>(flatMeasurements.Count);
        
        foreach (var measurement in flatMeasurements)
        {
            var nodeCatalog = catalogResults.FirstOrDefault(c => c.NodeId == measurement.NodeId);
            List<string> modelIds = [];
            
            if (nodeCatalog != null)
            {
                // Find all models observed for this connection
                // Fallback: If connectionId is empty/unknown in measurement, we can't easily map, but connectionId should be populated.
                modelIds = nodeCatalog.Observations
                    .Where(o => string.Equals(o.ConnectionId, measurement.ConnectionId, StringComparison.OrdinalIgnoreCase))
                    .Select(o => string.IsNullOrWhiteSpace(o.PublicId) ? o.UpstreamId : o.PublicId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
            
            enrichedMeasurements.Add(measurement with { ModelIds = modelIds });
        }

        quotaStore.Set(new QuotaSnapshot(enrichedMeasurements, now));
```

- [ ] **Step 2: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogResult.cs src/OpenRussiaRouter.CollectorService/Application/Collection/NodeCatalogCollector.cs src/OpenRussiaRouter.CollectorService/Application/Collection/NodePollingWorker.cs
git commit -m "feat: map model ids to quota measurements by connection id"
```

---

### Task 4: Expose Percent Metrics in ProviderQuotaMeter

**Files:**
- Modify: `src/OpenRussiaRouter.CollectorService/Application/Quota/ProviderQuotaMeter.cs`

- [ ] **Step 1: Add orr_provider_quota_percent and orr_model_quota_percent**

Update `ProviderQuotaMeter` to define the new gauges.

```csharp
using System.Diagnostics.Metrics;
using OpenRussiaRouter.BuildingBlocks.Metrics;

namespace OpenRussiaRouter.CollectorService.Application.Quota;

/// <summary>Exposes the latest quota snapshot as observable gauges.</summary>
public sealed class ProviderQuotaMeter : IDisposable
{
    private readonly Meter _meter;

    public ProviderQuotaMeter(QuotaSnapshotStore store)
    {
        _meter = new Meter(OpenTelemetryExtensions.DefaultMeterName);
        _meter.CreateObservableGauge(
            "orr_provider_quota",
            () => store.Current.Measurements.Select(m => new Measurement<double>(
                m.Remaining,
                new KeyValuePair<string, object?>("node_id", m.NodeId),
                new KeyValuePair<string, object?>("provider", m.Provider),
                new KeyValuePair<string, object?>("connection_id", m.ConnectionId))),
            unit: "tokens",
            description: "Remaining quota for providers");

        _meter.CreateObservableGauge(
            "orr_provider_quota_percent",
            () => store.Current.Measurements
                .Where(m => m.Percent.HasValue)
                .Select(m => new Measurement<double>(
                    m.Percent!.Value,
                    new KeyValuePair<string, object?>("node_id", m.NodeId),
                    new KeyValuePair<string, object?>("provider", m.Provider),
                    new KeyValuePair<string, object?>("connection_id", m.ConnectionId))),
            unit: "%",
            description: "Remaining quota percentage for providers");

        _meter.CreateObservableGauge(
            "orr_model_quota_percent",
            () => store.Current.Measurements
                .Where(m => m.Percent.HasValue)
                .SelectMany(m => m.ModelIds.Select(modelId => new Measurement<double>(
                    m.Percent!.Value,
                    new KeyValuePair<string, object?>("node_id", m.NodeId),
                    new KeyValuePair<string, object?>("model_id", modelId)))),
            unit: "%",
            description: "Remaining quota percentage per model");
    }

    public void Dispose() => _meter.Dispose();
}
```

- [ ] **Step 2: Commit**

```bash
git add src/OpenRussiaRouter.CollectorService/Application/Quota/ProviderQuotaMeter.cs
git commit -m "feat: expose percent metrics in ProviderQuotaMeter"
```
