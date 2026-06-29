namespace OpenRussiaRouter.ControlPlane.Domain;

public enum RouterNodeStatus
{
    Disabled = 0,
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3
}
