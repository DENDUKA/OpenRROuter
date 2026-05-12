using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Handles router node registry, health status and least-load leases.
/// </summary>
public sealed class RouterControlPlaneService(IControlPlaneRepository repository)
{
    private static readonly TimeSpan DefaultLeaseTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers a new external 9Router node.
    /// </summary>
    /// <param name="name">Display name.</param>
    /// <param name="endpointUrl">HTTPS endpoint.</param>
    /// <param name="tokenSecretName">Token secret reference.</param>
    /// <param name="capacityWeight">Relative capacity weight.</param>
    /// <param name="nowUtc">Creation timestamp.</param>
    /// <returns>Created node.</returns>
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

    /// <summary>
    /// Disables a router node.
    /// </summary>
    /// <param name="nodeId">Router node id.</param>
    public void DisableNode(string nodeId)
    {
        var node = GetRequiredNode(nodeId);
        node.Disable();
        repository.UpsertNode(node);
    }

    /// <summary>
    /// Applies latest health status.
    /// </summary>
    /// <param name="nodeId">Router node id.</param>
    /// <param name="status">Health status.</param>
    /// <param name="checkedAtUtc">Health check timestamp.</param>
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

    /// <summary>
    /// Selects the healthy node with the lowest normalized load and creates a lease.
    /// </summary>
    /// <param name="requestId">Gateway request id.</param>
    /// <param name="nowUtc">Current timestamp.</param>
    /// <returns>Router node lease.</returns>
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

    /// <summary>
    /// Releases an existing lease.
    /// </summary>
    /// <param name="leaseId">Lease id.</param>
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

    /// <summary>
    /// Lists router nodes for admin dashboard.
    /// </summary>
    /// <returns>Router node summaries.</returns>
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

    /// <summary>
    /// Lists nodes with secrets for the internal health checker.
    /// </summary>
    /// <returns>Router nodes.</returns>
    public IReadOnlyCollection<RouterNode> ListNodesForHealthChecks()
    {
        return repository.ListNodes(1000, null).Items;
    }

    /// <summary>
    /// Releases expired leases.
    /// </summary>
    /// <param name="nowUtc">Current timestamp.</param>
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
