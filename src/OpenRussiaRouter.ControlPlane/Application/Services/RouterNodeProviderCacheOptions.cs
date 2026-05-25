using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class RouterNodeProviderCacheOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(30);
}
