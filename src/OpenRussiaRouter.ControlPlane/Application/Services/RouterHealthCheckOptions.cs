using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class RouterHealthCheckOptions
{

    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    [Required]
    public string HealthPath { get; set; } = "/healthz";

    [Required]
    public string NodeTokenHeader { get; set; } = "X-ORR-Node-Token";

    public Dictionary<string, string> NodeTokens { get; set; } = new();
}
