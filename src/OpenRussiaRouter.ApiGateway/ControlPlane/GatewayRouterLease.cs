namespace OpenRussiaRouter.ApiGateway.ControlPlane;

public sealed record GatewayRouterLease(
    string LeaseId,
    string NodeId,
    Uri EndpointUrl,
    string ControlToken);

