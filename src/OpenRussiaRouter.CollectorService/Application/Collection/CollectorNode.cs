namespace OpenRussiaRouter.CollectorService.Application.Collection;

/// <summary>A node to poll, as projected from ControlPlane's node registry.</summary>
public sealed record CollectorNode(
    string NodeId,
    Uri EndpointUrl,
    string ControlToken,
    bool Enabled,
    string Status,
    bool HasBeenHealthChecked);
