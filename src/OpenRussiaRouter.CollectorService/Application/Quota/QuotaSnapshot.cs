namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record QuotaSnapshot(
    IReadOnlyList<QuotaMeasurement> Measurements,
    DateTimeOffset? CollectedAtUtc)
{
    public static readonly QuotaSnapshot Empty = new([], null);
}
