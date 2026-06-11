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
        store.Set(new QuotaSnapshot([
            new QuotaMeasurement("n1", "openai", "c1", 42d, 84d, ["model-1"]),
            new QuotaMeasurement("n1", "openai", "c1", 58d, 90d, ["model-2"])
        ], DateTimeOffset.UnixEpoch));

        using var meter = new ProviderQuotaMeter(store);
        var collected = new List<Measurement<double>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "OpenRussiaRouter" && instrument.Name == "orr_provider_quota_percent")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            collected.Add(new Measurement<double>(value, tags.ToArray())));
        listener.Start();

        listener.RecordObservableInstruments();

        collected.Should().ContainSingle();
        collected[0].Value.Should().Be(87d); // (84 + 90) / 2
        collected[0].Tags.ToArray().Should().Contain(t => t.Key == "provider" && (string)t.Value! == "openai");
    }
}
