using OpenRussiaRouter.Contracts.ControlPlane;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed record RouterNodeHealthResult(
    RouterNodeStatus Status,
    int? LatencyMs,
    string? ErrorCode);
