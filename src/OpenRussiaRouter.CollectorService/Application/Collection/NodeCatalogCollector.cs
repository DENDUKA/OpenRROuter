using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;

namespace OpenRussiaRouter.CollectorService.Application.Collection;

/// <summary>Collects a single node's model catalog with a per-node timeout and /v1/models fallback.</summary>
public sealed class NodeCatalogCollector(
    INodeAdminClient client,
    IOptionsMonitor<CollectorOptions> options,
    ILogger<NodeCatalogCollector> logger)
{
    public async Task<NodeCatalogResult> CollectAsync(CollectorNode node, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.CurrentValue.PerNodeTimeout);
        var nodeToken = timeoutCts.Token;

        NodeProvidersWithModelsResponse providersWithModels;
        try
        {
            providersWithModels = await client
                .ListProvidersWithModelsAsync(node.EndpointUrl, node.ControlToken, nodeToken)
                .ConfigureAwait(false);
        }
        catch (NodeAdminException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await CollectFallbackModelsAsync(node, nodeToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            logger.LogWarning(ex, "ListProvidersWithModels failed for node {NodeId}", node.NodeId);
            return new NodeCatalogResult(
                node.NodeId, Unreachable: true, [],
                FormatRequest(ex, client.DescribeListProvidersRequest(node.EndpointUrl)),
                FormatResponse(ex));
        }

        var observations = new List<ModelObservation>();
        foreach (var connection in providersWithModels.Connections)
        {
            if (!connection.IsActive || string.IsNullOrWhiteSpace(connection.Id))
                continue;
            foreach (var model in connection.Models)
            {
                if (string.IsNullOrWhiteSpace(model.Id) || !model.Enabled)
                    continue;
                observations.Add(new ModelObservation
                {
                    UpstreamId = model.Id,
                    PublicId = string.IsNullOrWhiteSpace(model.PublicId) ? string.Empty : model.PublicId,
                    DisplayName = model.Name ?? string.Empty,
                    ModelType = model.Type ?? string.Empty,
                    SourceProviders = model.Sources ?? [],
                    Rank = 2,
                    ConnectionId = connection.Id,
                    Provider = string.IsNullOrWhiteSpace(connection.Provider) ? string.Empty : connection.Provider,
                    SupportsReasoning = model.Caps?.Reasoning ?? false,
                    ThinkingFormat = model.Caps?.ThinkingFormat,
                    ThinkingCanDisable = model.Caps?.ThinkingCanDisable ?? true,
                    ThinkingOptions = model.Caps?.ThinkingOptions ?? []
                });
            }
        }

        return new NodeCatalogResult(node.NodeId, Unreachable: false, observations, null, null);
    }

    private async Task<NodeCatalogResult> CollectFallbackModelsAsync(
        CollectorNode node, CancellationToken nodeToken, CancellationToken cancellationToken)
    {
        try
        {
            var models = await client.ListModelsAsync(node.EndpointUrl, node.ControlToken, nodeToken).ConfigureAwait(false);
            var observations = models.Data
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => new ModelObservation
                {
                    UpstreamId = m.Id,
                    PublicId = m.Id,
                    Rank = 1
                })
                .ToList();
            return new NodeCatalogResult(node.NodeId, Unreachable: false, observations, null, null);
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Fallback model listing failed for node {NodeId}", node.NodeId);
            return new NodeCatalogResult(
                node.NodeId, Unreachable: true, [],
                FormatRequest(ex, client.DescribeListModelsRequest(node.EndpointUrl)),
                FormatResponse(ex));
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken) =>
        ex is NodeAdminException or InvalidOperationException or HttpRequestException ||
        (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static string FormatRequest(Exception ex, string fallbackRequest)
    {
        if (ex is NodeAdminException p && !string.IsNullOrWhiteSpace(p.RequestMethod) && !string.IsNullOrWhiteSpace(p.RequestUri))
            return $"{p.RequestMethod} {p.RequestUri}";
        return fallbackRequest;
    }

    private static string FormatResponse(Exception ex)
    {
        if (ex is NodeAdminException p)
        {
            var body = p.ResponseBody;
            const int maxBody = 1024;
            if (!string.IsNullOrEmpty(body) && body.Length > maxBody)
                body = body[..maxBody] + "… (truncated)";
            var statusLine = $"HTTP {(int)p.StatusCode} {p.StatusCode}";
            return string.IsNullOrWhiteSpace(body) ? statusLine : $"{statusLine}\n{body}";
        }
        if (ex is TaskCanceledException tce)
            return tce.InnerException is TimeoutException
                ? "TimeoutException: request timed out"
                : $"TaskCanceledException: {tce.Message}";
        if (ex is HttpRequestException hre)
            return $"HttpRequestException: {hre.Message}";
        if (ex is InvalidOperationException ioe)
            return $"InvalidOperationException: {ioe.Message}";
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    /// <summary>Enabled AND (never health-checked OR last check was not Unhealthy).</summary>
    public static bool IsRoutableForCatalog(CollectorNode node) =>
        node.Enabled &&
        (!node.HasBeenHealthChecked || !string.Equals(node.Status, "Unhealthy", StringComparison.OrdinalIgnoreCase));
}
