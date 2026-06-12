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
            var providerName = string.IsNullOrWhiteSpace(provider.Provider) ? "unknown" : provider.Provider;
            var connectionId = string.IsNullOrWhiteSpace(provider.ConnectionId) ? "unknown" : provider.ConnectionId;
            var accountName = string.IsNullOrWhiteSpace(provider.Name) ? connectionId : provider.Name;

            if (usage.TryGetProperty("quotas", out var quotasElement) &&
                quotasElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var quota in quotasElement.EnumerateObject())
                {
                    if (TryReadQuota(quota.Value, out var remaining, out var percent))
                    {
                        measurements.Add(new QuotaMeasurement(
                            node.NodeId,
                            providerName,
                            connectionId,
                            accountName,
                            quota.Name,
                            remaining,
                            percent,
                            [quota.Name]));
                    }
                }

                continue;
            }

            if (!TryReadQuota(usage, out var flatRemaining, out var flatPercent))
                continue;

            measurements.Add(new QuotaMeasurement(
                node.NodeId,
                providerName,
                connectionId,
                accountName,
                "total",
                flatRemaining,
                flatPercent,
                []));
        }
        return measurements;
    }

    private static bool TryReadQuota(System.Text.Json.JsonElement usage, out double remaining, out double? percent)
    {
        remaining = 0;
        percent = null;

        if (usage.TryGetProperty("remaining", out var remainingElement) &&
            remainingElement.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            remaining = remainingElement.GetDouble();
        }
        else if (usage.TryGetProperty("total", out var totalElement) &&
                 totalElement.ValueKind == System.Text.Json.JsonValueKind.Number &&
                 usage.TryGetProperty("used", out var usedElement) &&
                 usedElement.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            remaining = Math.Max(0, totalElement.GetDouble() - usedElement.GetDouble());
        }
        else
        {
            return false;
        }

        if (usage.TryGetProperty("remainingPercentage", out var remainingPercentageElement) &&
            remainingPercentageElement.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            percent = remainingPercentageElement.GetDouble();
        }
        else if (usage.TryGetProperty("limit", out var limitElement) &&
                 limitElement.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            var limit = limitElement.GetDouble();
            if (limit > 0)
            {
                percent = remaining / limit * 100.0;
            }
        }
        else if (usage.TryGetProperty("total", out var totalElement) &&
                 totalElement.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            var total = totalElement.GetDouble();
            if (total > 0)
            {
                percent = remaining / total * 100.0;
            }
        }

        return true;
    }
}
