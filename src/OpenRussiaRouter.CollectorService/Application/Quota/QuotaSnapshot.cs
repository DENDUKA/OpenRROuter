namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record QuotaSnapshot(
    IReadOnlyList<QuotaMeasurement> Measurements,
    DateTimeOffset? CollectedAtUtc,
    IReadOnlyList<ActiveModelQuotaMeasurement> ActiveModelMeasurements,
    IReadOnlyList<QuotaStatusMeasurement> Statuses,
    IReadOnlyList<NodeModelCatalogEntry> ModelCatalog)
{
    public QuotaSnapshot(IReadOnlyList<QuotaMeasurement> measurements, DateTimeOffset? collectedAtUtc)
        : this(measurements, collectedAtUtc, [], [], [])
    {
    }

    public QuotaSnapshot(
        IReadOnlyList<QuotaMeasurement> measurements,
        DateTimeOffset? collectedAtUtc,
        IReadOnlyList<ActiveModelQuotaMeasurement> activeModelMeasurements)
        : this(measurements, collectedAtUtc, activeModelMeasurements, [], [])
    {
    }

    public QuotaSnapshot(
        IReadOnlyList<QuotaMeasurement> measurements,
        DateTimeOffset? collectedAtUtc,
        IReadOnlyList<ActiveModelQuotaMeasurement> activeModelMeasurements,
        IReadOnlyList<QuotaStatusMeasurement> statuses)
        : this(measurements, collectedAtUtc, activeModelMeasurements, statuses, [])
    {
    }

    public static readonly QuotaSnapshot Empty = new([], null, [], [], []);
}
