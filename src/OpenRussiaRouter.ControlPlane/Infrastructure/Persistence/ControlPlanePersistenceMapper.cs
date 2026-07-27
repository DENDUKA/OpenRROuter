using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

public static class ControlPlanePersistenceMapper
{
    public static RouterNodeEntity ToEntity(RouterNode node)
    {
        return new RouterNodeEntity
        {
            Id = node.Id,
            Name = node.Name,
            EndpointUrl = node.EndpointUrl.ToString(),
            ControlToken = node.ControlToken,
            CapacityWeight = node.CapacityWeight,
            Enabled = node.Enabled,
            Status = node.Status,
            InFlightRequests = node.InFlightRequests,
            LastCheckedAtUtc = node.LastCheckedAtUtc,
            LastHealthLatencyMs = node.LastHealthLatencyMs,
            LastHealthErrorCode = node.LastHealthErrorCode,
            CreatedAtUtc = node.CreatedAtUtc
        };
    }

    public static RouterNode ToDomain(RouterNodeEntity entity)
    {
        var node = new RouterNode(
            entity.Id,
            entity.Name,
            new Uri(entity.EndpointUrl),
            entity.ControlToken,
            entity.CapacityWeight,
            entity.CreatedAtUtc);

        if (entity.Status != RouterNodeStatus.Unhealthy || entity.LastCheckedAtUtc.HasValue)
        {
            node.ApplyHealth(
                entity.Status,
                entity.LastCheckedAtUtc ?? entity.CreatedAtUtc,
                entity.LastHealthLatencyMs,
                entity.LastHealthErrorCode);
        }

        if (!entity.Enabled)
            node.Disable();

        for (var i = 0; i < entity.InFlightRequests; i++)
        {
            node.Lease();
        }

        return node;
    }

    public static RouterLeaseEntity ToEntity(RouterLease lease)
    {
        return new RouterLeaseEntity
        {
            Id = lease.Id,
            NodeId = lease.NodeId,
            RequestId = lease.RequestId,
            ExpiresAtUtc = lease.ExpiresAtUtc,
            CreatedAtUtc = lease.CreatedAtUtc
        };
    }

    public static RouterLease ToDomain(RouterLeaseEntity entity)
    {
        return new RouterLease(entity.Id, entity.NodeId, entity.RequestId, entity.ExpiresAtUtc, entity.CreatedAtUtc);
    }
}
