using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application;

/// <summary>
/// Configuration for stale router lease cleanup.
/// </summary>
public sealed class RouterLeaseCleanupOptions
{
    /// <summary>
    /// Whether periodic lease cleanup is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Delay between cleanup rounds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
}
