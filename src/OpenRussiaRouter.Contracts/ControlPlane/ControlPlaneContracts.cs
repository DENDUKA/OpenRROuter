namespace OpenRussiaRouter.Contracts.ControlPlane;

public sealed record RouterNodeLease(
    string LeaseId,
    string NodeId,
    Uri EndpointUrl,
    string ControlToken);

