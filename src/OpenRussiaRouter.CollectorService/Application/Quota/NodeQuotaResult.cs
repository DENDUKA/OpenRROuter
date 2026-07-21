namespace OpenRussiaRouter.CollectorService.Application.Quota;

/// <summary>
/// Результат опроса квот одной ноды: сами измерения плюс отчёт по КАЖДОМУ подключению —
/// включая те, у которых квоту прочитать не удалось (они не дают <see cref="QuotaMeasurement"/>,
/// но обязаны быть видимы).
/// </summary>
public sealed record NodeQuotaResult(
    IReadOnlyList<QuotaMeasurement> Measurements,
    IReadOnlyList<QuotaStatusMeasurement> Statuses)
{
    public static readonly NodeQuotaResult Empty = new([], []);
}
