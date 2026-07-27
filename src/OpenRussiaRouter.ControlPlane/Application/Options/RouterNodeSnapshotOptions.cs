using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application.Options;

public sealed class RouterNodeSnapshotOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(10);
}
