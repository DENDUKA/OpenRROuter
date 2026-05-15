namespace OpenRussiaRouter.ControlPlane.Application.Services;

public interface IRouterKeySyncService
{
    Task SyncAllAsync(CancellationToken ct);
    Task SyncToNodeAsync(string nodeId, CancellationToken ct);
    Task RevokeKeyAsync(string prefix, CancellationToken ct);
}
