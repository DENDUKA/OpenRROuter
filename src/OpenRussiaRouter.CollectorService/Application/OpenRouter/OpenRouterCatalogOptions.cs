using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.CollectorService.Application.OpenRouter;

public sealed class OpenRouterCatalogOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:01:00", "06:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(30);
}
