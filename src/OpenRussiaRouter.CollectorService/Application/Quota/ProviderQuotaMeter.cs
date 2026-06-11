using System.Diagnostics.Metrics;
using OpenRussiaRouter.BuildingBlocks.Metrics;

namespace OpenRussiaRouter.CollectorService.Application.Quota;

/// <summary>Exposes the latest quota snapshot as the observable gauge `orr_provider_quota`.</summary>
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
