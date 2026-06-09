using OpenRussiaRouter.Contracts.ControlPlane;

namespace OpenRussiaRouter.ControlPlane.Application.Contracts;

public sealed record RouterNodeHealthResult(
    RouterNodeStatus Status,
    int? LatencyMs,
    string? ErrorCode);
