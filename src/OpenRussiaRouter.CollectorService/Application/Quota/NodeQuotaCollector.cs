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
    public async Task<NodeQuotaResult> CollectAsync(CollectorNode node, CancellationToken cancellationToken)
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

            // Нода целиком недоступна: конкретных подключений мы не знаем, поэтому отдаём одну
            // запись на ноду — иначе падение ноды выглядит как «квот просто нет».
            return new NodeQuotaResult(
                [],
                [new QuotaStatusMeasurement(
                    node.NodeId,
                    "unknown",
                    "unknown",
                    "unknown",
                    Reporting: false,
                    QuotaUnavailableReasons.NodeUnreachable)]);
        }

        var measurements = new List<QuotaMeasurement>();
        var statuses = new List<QuotaStatusMeasurement>();

        foreach (var provider in response.Providers)
        {
            var providerName = string.IsNullOrWhiteSpace(provider.Provider) ? "unknown" : provider.Provider;
            var connectionId = string.IsNullOrWhiteSpace(provider.ConnectionId) ? "unknown" : provider.ConnectionId;
            var accountName = string.IsNullOrWhiteSpace(provider.Name) ? connectionId : provider.Name;

            void Status(bool reporting, string reason) => statuses.Add(new QuotaStatusMeasurement(
                node.NodeId, providerName, connectionId, accountName, reporting, reason));

            if (provider.Usage is not { } usage || usage.ValueKind == System.Text.Json.JsonValueKind.Null)
            {
                Status(false, QuotaUnavailableReasons.NoUsage);
                continue;
            }

            if (usage.TryGetProperty("quotas", out var quotasElement) &&
                quotasElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var parsed = 0;
                foreach (var quota in quotasElement.EnumerateObject())
                {
                    if (TryReadQuota(quota.Value, out var remaining, out var percent))
                    {
                        parsed++;
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

                // Пустой quotas — характерный признак протухшей авторизации у провайдера
                // (нода отвечает 200 и кладёт текст ошибки в usage.message).
                Status(parsed > 0, parsed > 0
                    ? QuotaUnavailableReasons.Ok
                    : QuotaUnavailableReasons.EmptyQuotas);
                continue;
            }

            if (!TryReadQuota(usage, out var flatRemaining, out var flatPercent))
            {
                Status(false, QuotaUnavailableReasons.NoQuotaFields);
                continue;
            }

            measurements.Add(new QuotaMeasurement(
                node.NodeId,
                providerName,
                connectionId,
                accountName,
                "total",
                flatRemaining,
                flatPercent,
                []));
            Status(true, QuotaUnavailableReasons.Ok);
        }

        return new NodeQuotaResult(measurements, statuses);
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
