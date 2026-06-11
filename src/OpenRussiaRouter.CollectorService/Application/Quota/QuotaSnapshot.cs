namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record QuotaSnapshot(
    IReadOnlyList<QuotaMeasurement> Measurements,
    DateTimeOffset? CollectedAtUtc,
    IReadOnlyList<ActiveModelQuotaMeasurement> ActiveModelMeasurements)
{
    public QuotaSnapshot(IReadOnlyList<QuotaMeasurement> measurements, DateTimeOffset? collectedAtUtc)
        : this(measurements, collectedAtUtc, [])
    {
    }

    public static readonly QuotaSnapshot Empty = new([], null, []);
}
