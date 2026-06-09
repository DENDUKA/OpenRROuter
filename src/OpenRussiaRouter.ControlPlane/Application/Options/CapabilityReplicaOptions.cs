using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application.Options;

public sealed class CapabilityReplicaOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
}
