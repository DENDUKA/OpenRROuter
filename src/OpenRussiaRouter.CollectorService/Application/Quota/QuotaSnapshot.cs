namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record QuotaSnapshot(
    IReadOnlyList<QuotaMeasurement> Measurements,
    DateTimeOffset? CollectedAtUtc,
    IReadOnlyList<ActiveModelQuotaMeasurement> ActiveModelMeasurements,
    IReadOnlyList<QuotaStatusMeasurement> Statuses)
{
    public QuotaSnapshot(IReadOnlyList<QuotaMeasurement> measurements, DateTimeOffset? collectedAtUtc)
        : this(measurements, collectedAtUtc, [], [])
    {
    }

    public QuotaSnapshot(
        IReadOnlyList<QuotaMeasurement> measurements,
        DateTimeOffset? collectedAtUtc,
        IReadOnlyList<ActiveModelQuotaMeasurement> activeModelMeasurements)
        : this(measurements, collectedAtUtc, activeModelMeasurements, [])
    {
    }

    public static readonly QuotaSnapshot Empty = new([], null, [], []);
}
