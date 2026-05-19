using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class RouterControlPlaneService(IControlPlaneRepository repository)
{
    private static readonly TimeSpan DefaultLeaseTtl = TimeSpan.FromMinutes(5);

    public RouterNode RegisterNode(
        string name,
        Uri endpointUrl,
        string tokenSecretName,
        int capacityWeight,
        DateTimeOffset nowUtc)
    {
        var node = new RouterNode(
            Guid.NewGuid().ToString("N"),
            name,
            endpointUrl,
            tokenSecretName,
            capacityWeight,
            nowUtc);

        repository.UpsertNode(node);
        return node;
    }

    public void DisableNode(string nodeId)
    {
        var node = GetRequiredNode(nodeId);
        node.Disable();
        repository.UpsertNode(node);
    }

    public void EnableNode(string nodeId)
    {
        var node = GetRequiredNode(nodeId);
        node.Enable();
        repository.UpsertNode(node);
    }

    public void DeleteNode(string nodeId)
    {
        if (!repository.RemoveNode(nodeId))
            throw new KeyNotFoundException("Router node was not found.");
    }

    public void ApplyHealth(
        string nodeId,
        RouterNodeStatus status,
        DateTimeOffset checkedAtUtc,
        int? latencyMs = null,
        string? errorCode = null)
    {
        var node = GetRequiredNode(nodeId);
        node.ApplyHealth(status, checkedAtUtc, latencyMs, errorCode);
        repository.UpsertNode(node);
    }

    public RouterNodeLease SelectRouterNode(string requestId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("Value cannot be empty.", nameof(requestId));
        }

        ReleaseExpiredLeases(nowUtc);

        var node = repository.ListNodes(1000, null)
            .Items
            .Where(candidate => candidate.Enabled && candidate.Status == RouterNodeStatus.Healthy)
            .OrderBy(candidate => (decimal)candidate.InFlightRequests / candidate.CapacityWeight)
            .ThenBy(candidate => candidate.InFlightRequests)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (node is null)
        {
            throw new InvalidOperationException("No healthy router nodes are available.");
        }

        node.Lease();
        repository.UpsertNode(node);
        var lease = new RouterLease(
            Guid.NewGuid().ToString("N"),
            node.Id,
            requestId.Trim(),
            nowUtc.Add(DefaultLeaseTtl),
            nowUtc);
        repository.AddLease(lease);

        return new RouterNodeLease(lease.Id, node.Id, node.EndpointUrl, node.TokenSecretName);
    }

    public void ReleaseRouterNode(string leaseId)
    {
        var lease = repository.RemoveLease(leaseId);
        if (lease is null)
        {
            return;
        }

        if (repository.GetNode(lease.NodeId) is { } node)
        {
            node.Release();
            repository.UpsertNode(node);
        }
    }

    public IReadOnlyCollection<RouterNodeSummary> ListRouterNodes()
    {
        return repository.ListNodes(1000, null)
            .Items
            .Select(node => new RouterNodeSummary(
                node.Id,
                node.Name,
                node.EndpointUrl,
                node.Enabled ? node.Status : RouterNodeStatus.Disabled,
                node.InFlightRequests,
                node.LastCheckedAtUtc))
            .ToArray();
    }

    public RouterNode? GetRouterNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        return repository.GetNode(nodeId.Trim());
    }

    public IReadOnlyCollection<RouterNode> ListNodesForHealthChecks()
    {
        return repository.ListNodes(1000, null).Items;
    }

    public void ReleaseExpiredLeases(DateTimeOffset nowUtc)
    {
        var expiredLeases = repository.RemoveExpiredLeases(nowUtc);
        if (expiredLeases.Count == 0)
        {
            return;
        }

        var nodeIds = expiredLeases.Select(l => l.NodeId).Distinct().ToArray();
        repository.ReleaseNodes(nodeIds);
    }

    private RouterNode GetRequiredNode(string nodeId)
    {
        return repository.GetNode(nodeId) ?? throw new KeyNotFoundException("Router node was not found.");
    }
}
