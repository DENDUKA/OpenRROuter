using OpenRussiaRouter.Contracts.ControlPlane;

namespace OpenRussiaRouter.ControlPlane.Application;

/// <summary>
/// Result of one health check request to a 9Router node.
/// </summary>
/// <param name="Status">Observed node status.</param>
/// <param name="LatencyMs">Round-trip latency in milliseconds.</param>
/// <param name="ErrorCode">Stable error code for diagnostics.</param>
public sealed record RouterNodeHealthResult(
    RouterNodeStatus Status,
    int? LatencyMs,
    string? ErrorCode);
