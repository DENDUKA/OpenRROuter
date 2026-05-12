using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application;

public sealed class RouterLeaseCleanupOptions
{

    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
}
