using OpenRussiaRouter.ControlPlane.Domain;

namespace OpenRussiaRouter.ControlPlane.Application.Contracts;

public sealed record RouterNodeHealthResult(
    RouterNodeStatus Status,
    int? LatencyMs,
    string? ErrorCode);
