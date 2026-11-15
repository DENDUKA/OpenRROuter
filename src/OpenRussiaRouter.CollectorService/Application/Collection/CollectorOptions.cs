using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.CollectorService.Application.Collection;

public sealed class CollectorOptions
{
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan NodePollInterval { get; set; } = TimeSpan.FromMinutes(1);

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan NodeListRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    [Range(typeof(TimeSpan), "00:00:30", "06:00:00")]
    public TimeSpan ActiveModelsRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan PerNodeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Keep-last-good: если нода пропала из цикла сбора (проба не удалась или
    /// нода временно не routable), её последний успешный каталог переносится
    /// в новый снимок, пока он не старше этого окна. Без переноса одиночный
    /// сбойный цикл стирал ВСЕ модели ноды из capability-store до следующего
    /// успешного сбора (~1 минута "No router available"). Безопасно: hot-path
    /// роутинга отдельно фильтрует ноды по их health из control plane.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan KeepLastGoodCatalogsFor { get; set; } = TimeSpan.FromMinutes(10);
}
