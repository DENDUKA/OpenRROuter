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
    public TimeSpan PerNodeTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
