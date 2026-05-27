using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class RouterModelCapabilityOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
