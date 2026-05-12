namespace OpenRussiaRouter.ApiGateway.ControlPlane;

/// <summary>
/// Router lease returned by Control Plane for one Gateway request.
/// </summary>
/// <param name="LeaseId">Lease id.</param>
/// <param name="NodeId">Router node id.</param>
/// <param name="EndpointUrl">Router node base endpoint.</param>
/// <param name="TokenSecretName">Secret reference for node token.</param>
public sealed record GatewayRouterLease(
    string LeaseId,
    string NodeId,
    Uri EndpointUrl,
    string TokenSecretName);

