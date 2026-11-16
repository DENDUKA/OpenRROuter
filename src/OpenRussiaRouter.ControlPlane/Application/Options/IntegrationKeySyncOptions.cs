using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ControlPlane.Application.Options;

/// <summary>
/// Periodic reconciliation of integration keys on every enabled node.
///
/// The immediate push in <c>PushUserIntegrationKey</c> covers the normal path, but it is
/// best-effort by design: WebApp logs a failed push and still returns the key to the user
/// (the key exists in AuthService, so refusing to show it would be worse). Without a
/// reconciler a node that was down, misconfigured, or newly registered — it starts with an
/// empty key table — keeps answering 401 to every proxied request forever, and nothing ever
/// retries. That is exactly how the kRouter node served 401 while the fleet's key table had
/// drifted by 351 rows.
/// </summary>
public sealed class IntegrationKeySyncOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:01:00", "24:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Wait before the first round. A fresh ControlPlane start would otherwise race AuthService
    /// (gRPC not listening yet) and burn the first, most useful attempt.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:30:00")]
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);
}
