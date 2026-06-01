using System.Diagnostics.Metrics;
using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Quota;

namespace OpenRussiaRouter.CollectorService.Tests.Quota;

public class ProviderQuotaMeterTests
{
    [Fact]
    public void Gauge_PublishesMeasurementsFromStore()
    {
        var store = new QuotaSnapshotStore();
        store.Set(new QuotaSnapshot([new QuotaMeasurement("n1", "openai", "c1", 42d)], DateTimeOffset.UnixEpoch));

        using var meter = new ProviderQuotaMeter(store);
        var collected = new List<Measurement<double>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "OpenRussiaRouter" && instrument.Name == "orr_provider_quota")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            collected.Add(new Measurement<double>(value, tags.ToArray())));
        listener.Start();

        listener.RecordObservableInstruments();

        collected.Should().ContainSingle(m => m.Value == 42d);
    }
}
