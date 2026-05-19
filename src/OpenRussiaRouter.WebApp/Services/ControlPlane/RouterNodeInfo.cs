namespace OpenRussiaRouter.WebApp.Services.ControlPlane;

public sealed record RouterNodeInfo(
    string NodeId,
    string Name,
    string EndpointUrl,
    string Status,
    int InFlightRequests,
    DateTimeOffset? LastCheckedAt,
    string ControlToken = "",
    int CapacityWeight = 0,
    string LastHealthErrorCode = "");
