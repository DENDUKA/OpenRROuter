using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application.Options;

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

    /// <summary>
    /// Количество подряд идущих неудачных проверок, после которого нода переводится в
    /// Unhealthy/Degraded. До достижения порога нода остаётся routable: одиночный таймаут
    /// или сетевой сбой на одном пробе не должен снимать её с маршрутизации до следующего
    /// успешного чека (иначе ~1 минута "No router available" на все её модели).
    /// </summary>
    [Range(1, 10)]
    public int UnhealthyThreshold { get; set; } = 3;

    [Range(1, 100)]
    public int MaxConcurrency { get; set; } = 10;
}
