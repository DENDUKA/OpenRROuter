using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public interface IControlPlaneRepository
{

    void UpsertNode(RouterNode node);

    RouterNode? GetNode(string nodeId);

    (IReadOnlyCollection<RouterNode> Items, bool HasMore) ListNodes(int limit, string? afterName = null);

    void AddLease(RouterLease lease);

    RouterLease? RemoveLease(string leaseId);

    IReadOnlyCollection<RouterLease> RemoveExpiredLeases(DateTimeOffset nowUtc);

    void ReleaseNodes(IReadOnlyCollection<string> nodeIds);
}