using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Quartz job that periodically polls every enabled router node for its supported models
/// and updates <see cref="NodeModelCapabilityStore"/> so the routing layer can filter
/// candidates by model availability.
/// </summary>
[DisallowConcurrentExecution]
public sealed class RouterModelCapabilityJob(
    RouterControlPlaneService controlPlane,
    IRouterNodeProvider providerClient,
    NodeModelCapabilityStore capabilityStore,
    IOptions<RouterModelCapabilityOptions> options,
    ILogger<RouterModelCapabilityJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!options.Value.Enabled)
            return;

        var ct = context.CancellationToken;
        var nodes = await controlPlane.ListNodesForHealthChecksAsync(ct).ConfigureAwait(false);

        foreach (var node in nodes.Where(n => n.Enabled))
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(options.Value.Timeout);

            try
            {
                var modelIds = await CollectModelsAsync(node, timeoutCts.Token).ConfigureAwait(false);
                capabilityStore.SetNodeModels(node.Id, modelIds);
                logger.LogInformation(
                    "Collected {Count} models from node {NodeId} ({NodeName})",
                    modelIds.Count, node.Id, node.Name);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Keep stale data — do NOT clear the node. A transient failure should not
                // remove a healthy node from the routing pool.
                logger.LogWarning(ex,
                    "Failed to collect model capabilities from node {NodeId} ({NodeName})",
                    node.Id, node.Name);
            }
        }
    }

    private async Task<IReadOnlySet<string>> CollectModelsAsync(
        Domain.Entities.RouterNode node,
        CancellationToken ct)
    {
        try
        {
            var response = await providerClient
                .ListProvidersWithModelsAsync(node, ct)
                .ConfigureAwait(false);

            return response.Connections
                .Where(c => c.IsActive)
                .SelectMany(c => c.Models)
                .Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => string.IsNullOrWhiteSpace(m.PublicId) ? m.Id : m.PublicId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (RouterNodeProviderException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Node does not expose the admin API — fall back to /v1/models
            logger.LogDebug(
                "Node {NodeId} does not support providers/with-models, falling back to /v1/models",
                node.Id);

            var fallback = await providerClient.ListModelsAsync(node, ct).ConfigureAwait(false);
            return fallback.Data
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => m.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
