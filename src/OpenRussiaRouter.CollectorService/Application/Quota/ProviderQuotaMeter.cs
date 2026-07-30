using System.Diagnostics.Metrics;
using OpenRussiaRouter.BuildingBlocks.Metrics;

namespace OpenRussiaRouter.CollectorService.Application.Quota;

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
                new KeyValuePair<string, object?>("connection_id", m.ConnectionId),
                new KeyValuePair<string, object?>("account_name", m.AccountName),
                new KeyValuePair<string, object?>("quota_name", m.QuotaName))),
            unit: "tokens",
            description: "Remaining quota for providers");

        _meter.CreateObservableGauge(
            "orr_provider_quota_percent",
            () => store.Current.Measurements
                .Where(m => m.Percent.HasValue)
                .GroupBy(m => new { m.NodeId, m.Provider, m.ConnectionId, m.AccountName, m.QuotaName })
                .Select(g => new Measurement<double>(
                    g.Average(m => m.Percent!.Value),
                    new KeyValuePair<string, object?>("node_id", g.Key.NodeId),
                    new KeyValuePair<string, object?>("provider", g.Key.Provider),
                    new KeyValuePair<string, object?>("connection_id", g.Key.ConnectionId),
                    new KeyValuePair<string, object?>("account_name", g.Key.AccountName),
                    new KeyValuePair<string, object?>("quota_name", g.Key.QuotaName))),
            unit: "%",
            description: "Remaining quota percentage for providers");

        // 1 = квота читается, 0 = подключение живёт, но чисел не отдаёт. Без этого гейджа
        // сломанный провайдер просто пропадал из всех остальных метрик, и «квот нет» было
        // неотличимо от «квоты сломаны».
        _meter.CreateObservableGauge(
            "orr_provider_quota_status",
            () => store.Current.Statuses.Select(s => new Measurement<int>(
                s.Reporting ? 1 : 0,
                new KeyValuePair<string, object?>("node_id", s.NodeId),
                new KeyValuePair<string, object?>("provider", s.Provider),
                new KeyValuePair<string, object?>("connection_id", s.ConnectionId),
                new KeyValuePair<string, object?>("account_name", s.AccountName),
                new KeyValuePair<string, object?>("reason", s.Reason))),
            description: "1 when a provider connection reports quota, 0 when it cannot");

        _meter.CreateObservableGauge(
            "orr_model_active",
            () => store.Current.Measurements
                .SelectMany(m => m.ModelIds)
                .Distinct()
                .Select(modelId => new Measurement<long>(
                    1,
                    new KeyValuePair<string, object?>("model_id", modelId))),
            description: "Indicates if a model is currently active/available");

        _meter.CreateObservableGauge(
            "orr_model_quota_percent",
            () => store.Current.Measurements
                .Where(m => m.Percent.HasValue)
                .SelectMany(m => m.ModelIds.Select(modelId => new Measurement<double>(
                    m.Percent!.Value,
                    new KeyValuePair<string, object?>("node_id", m.NodeId),
                    new KeyValuePair<string, object?>("provider", m.Provider),
                    new KeyValuePair<string, object?>("model_id", modelId)))),
            unit: "%",
            description: "Remaining quota percentage per model");

        // Полный срез «что нода отдаёт на подключении» с состоянием модели в каталоге ORR.
        // Кардинальность = ноды × подключения × модели; user/api-key сюда добавлять нельзя.
        _meter.CreateObservableGauge(
            "orr_node_model_catalog",
            () => store.Current.ModelCatalog.Select(entry => new Measurement<int>(
                1,
                new KeyValuePair<string, object?>("node_id", entry.NodeId),
                new KeyValuePair<string, object?>("provider", entry.Provider),
                new KeyValuePair<string, object?>("connection_id", entry.ConnectionId),
                new KeyValuePair<string, object?>("model_id", entry.ModelId),
                new KeyValuePair<string, object?>("router_model_id", entry.RouterModelId),
                new KeyValuePair<string, object?>("billing_state", entry.BillingState))),
            description: "Models a node serves per provider connection, labelled by their state in the ORR catalog");

        _meter.CreateObservableGauge(
            "orr_active_model_quota_percent",
            () => store.Current.ActiveModelMeasurements.Select(m => new Measurement<double>(
                m.Percent,
                new KeyValuePair<string, object?>("node_id", m.NodeId),
                new KeyValuePair<string, object?>("provider", m.Provider),
                new KeyValuePair<string, object?>("connection_id", m.ConnectionId),
                new KeyValuePair<string, object?>("model_id", m.ModelId),
                new KeyValuePair<string, object?>("router_model_id", m.RouterModelId))),
            unit: "%",
            description: "Remaining quota percentage per active admin model and provider connection");
    }

    public void Dispose() => _meter.Dispose();
}
