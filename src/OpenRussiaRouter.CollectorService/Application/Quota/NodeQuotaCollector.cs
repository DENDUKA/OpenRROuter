using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;

namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed class NodeQuotaCollector(
    INodeAdminClient client,
    IOptionsMonitor<CollectorOptions> options,
    ILogger<NodeQuotaCollector> logger)
{
    public async Task<IReadOnlyList<QuotaMeasurement>> CollectAsync(CollectorNode node, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.CurrentValue.PerNodeTimeout);

        NodeProviderQuotaResponse response;
        try
        {
            response = await client
                .GetProviderQuotaAsync(node.EndpointUrl, node.ControlToken, includeInactive: false, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Quota collection failed for node {NodeId}", node.NodeId);
            return [];
        }

        var measurements = new List<QuotaMeasurement>();
        foreach (var provider in response.Providers)
        {
            if (provider.Usage is not { } usage || usage.ValueKind == System.Text.Json.JsonValueKind.Null)
                continue;
            if (!usage.TryGetProperty("remaining", out var remainingElement))
                continue;
            if (remainingElement.ValueKind is not (System.Text.Json.JsonValueKind.Number))
                continue;

            measurements.Add(new QuotaMeasurement(
                node.NodeId,
                string.IsNullOrWhiteSpace(provider.Provider) ? "unknown" : provider.Provider,
                string.IsNullOrWhiteSpace(provider.ConnectionId) ? "unknown" : provider.ConnectionId,
                remainingElement.GetDouble()));
        }
        return measurements;
    }
}
