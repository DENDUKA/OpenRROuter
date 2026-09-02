using System.Diagnostics.Metrics;
using OpenRussiaRouter.BuildingBlocks.Metrics;

namespace OpenRussiaRouter.BillingService.Infrastructure.YandexMetrika;

internal static class YandexMetrikaOfflineConversionMetrics
{
    private static readonly Meter Meter = new(OpenTelemetryExtensions.DefaultMeterName);

    private static readonly Counter<long> Transitions = Meter.CreateCounter<long>(
        "orr_yandex_metrika_offline_conversion_transitions_total",
        description: "Yandex Metrica offline conversion outbox transitions");

    public static void RecordTransition(string target, string status) =>
        Transitions.Add(
            1,
            new KeyValuePair<string, object?>("target", target),
            new KeyValuePair<string, object?>("status", status));
}
