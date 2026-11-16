using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;
using OpenRussiaRouter.ControlPlane.Application.Contracts;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// One-shot reconciliation of a node's integration keys against the authoritative key set.
///
/// Extracted from the gRPC facade so the same code backs both the operator-triggered
/// <c>SyncNodeIntegrationKeys</c> RPC and the periodic <c>IntegrationKeySyncWorker</c>. The
/// worker must behave identically to the button an operator presses, or the two disagree
/// about what "in sync" means.
/// </summary>
public sealed class IntegrationKeySyncService(
    RouterControlPlaneService controlPlane,
    IRouterNodeProvider providerClient,
    IIntegrationKeySource keySource,
    ILogger<IntegrationKeySyncService> logger)
{
    /// <summary>
    /// Pushes every non-deleted key to one node.
    /// </summary>
    /// <returns>
    /// The node's own counters, or <c>null</c> when the node is unknown, the key source is
    /// unavailable, or the node rejects the push. A <c>null</c> result is a normal outcome to
    /// be logged and retried next round, not an error to propagate — one bad node must not
    /// stop a sweep over the rest.
    /// </returns>
    public async Task<RouterIntegrationKeysSyncResponse?> SyncNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        var node = await controlPlane.GetRouterNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
        return node is null ? null : await SyncNodeAsync(node, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RouterIntegrationKeysSyncResponse?> SyncNodeAsync(
        RouterNode node,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        var keys = await keySource.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (keys is null)
            return null;

        try
        {
            return await providerClient.SyncIntegrationKeysAsync(node, keys, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RouterNodeProviderException or HttpRequestException or InvalidOperationException)
        {
            // Keep the node's own error detail: "which node, what it answered" is the only
            // way to tell a down node from one that rejects the payload.
            logger.LogWarning(
                ex,
                "Failed to sync integration keys to node {NodeId} ({NodeName}): {ErrorMessage}",
                node.Id,
                node.Name,
                ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Nodes worth syncing: enabled only. A disabled node is deliberately out of routing, and
    /// its key table gets filled by the first sweep after it is enabled again.
    /// </summary>
    public async Task<IReadOnlyList<RouterNode>> ListEnabledNodesAsync(CancellationToken cancellationToken = default)
    {
        var all = await controlPlane.ListNodesForHealthChecksAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(n => n.Enabled).ToList();
    }
}
