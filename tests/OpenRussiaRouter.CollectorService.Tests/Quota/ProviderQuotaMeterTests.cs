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

    [Fact]
    public void ActiveModelGauge_PublishesModelProviderNodeLabels()
    {
        var store = new QuotaSnapshotStore();
        store.Set(new QuotaSnapshot(
            [],
            DateTimeOffset.UnixEpoch,
            [new ActiveModelQuotaMeasurement("n1", "openai", "c1", "ORR/GPT", "router/gpt", 42d)]));

        using var meter = new ProviderQuotaMeter(store);
        var collected = new List<Measurement<double>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "OpenRussiaRouter" && instrument.Name == "orr_active_model_quota_percent")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            collected.Add(new Measurement<double>(value, tags.ToArray())));
        listener.Start();

        listener.RecordObservableInstruments();

        collected.Should().ContainSingle();
        collected[0].Value.Should().Be(42d);
        collected[0].Tags.ToArray().Should().Contain(t => t.Key == "node_id" && (string)t.Value! == "n1");
        collected[0].Tags.ToArray().Should().Contain(t => t.Key == "provider" && (string)t.Value! == "openai");
        collected[0].Tags.ToArray().Should().Contain(t => t.Key == "connection_id" && (string)t.Value! == "c1");
        collected[0].Tags.ToArray().Should().Contain(t => t.Key == "model_id" && (string)t.Value! == "ORR/GPT");
        collected[0].Tags.ToArray().Should().Contain(t => t.Key == "router_model_id" && (string)t.Value! == "router/gpt");
    }
}
