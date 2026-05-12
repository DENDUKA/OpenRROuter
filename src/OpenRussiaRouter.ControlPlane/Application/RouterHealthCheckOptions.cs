using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application;

/// <summary>
/// Configuration for external 9Router node health checks.
/// </summary>
public sealed class RouterHealthCheckOptions
{
    /// <summary>
    /// Whether periodic health checks are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Delay between health check rounds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Per-node HTTP timeout.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Relative health endpoint path on each 9Router node.
    /// </summary>
    [Required]
    public string HealthPath { get; set; } = "/healthz";

    /// <summary>
    /// Internal token header expected by 9Router.
    /// </summary>
    [Required]
    public string NodeTokenHeader { get; set; } = "X-ORR-Node-Token";

    /// <summary>
    /// Secret-name to raw-token mapping for MVP local configuration.
    /// </summary>
    public Dictionary<string, string> NodeTokens { get; set; } = new(StringComparer.Ordinal);
}
