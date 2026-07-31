using System.Globalization;
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
                    if (TryReadQuota(quota.Value, out var numbers))
                    {
                        parsed++;
                        measurements.Add(new QuotaMeasurement(
                            node.NodeId,
                            providerName,
                            connectionId,
                            accountName,
                            quota.Name,
                            numbers.Remaining,
                            numbers.Percent,
                            [quota.Name],
                            numbers.Total,
                            numbers.ResetAt));
                    }
                }

                // Пустой quotas — характерный признак протухшей авторизации у провайдера
                // (нода отвечает 200 и кладёт текст ошибки в usage.message).
                Status(parsed > 0, parsed > 0
                    ? QuotaUnavailableReasons.Ok
                    : QuotaUnavailableReasons.EmptyQuotas);
                continue;
            }

            if (!TryReadQuota(usage, out var flat))
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
                flat.Remaining,
                flat.Percent,
                [],
                flat.Total,
                flat.ResetAt));
            Status(true, QuotaUnavailableReasons.Ok);
        }

        return new NodeQuotaResult(measurements, statuses);
    }

    /// <summary>Числа одной квоты. Record вместо четырёх out-параметров.</summary>
    private readonly record struct QuotaNumbers(
        double Remaining,
        double? Percent,
        double? Total,
        DateTimeOffset? ResetAt);

    private static bool TryReadQuota(System.Text.Json.JsonElement usage, out QuotaNumbers numbers)
    {
        numbers = default;

        var total = ReadNumber(usage, "total") ?? ReadNumber(usage, "limit");
        double remaining;

        if (ReadNumber(usage, "remaining") is { } explicitRemaining)
        {
            remaining = explicitRemaining;
        }
        else if (total is { } totalValue && ReadNumber(usage, "used") is { } used)
        {
            remaining = Math.Max(0, totalValue - used);
        }
        else
        {
            return false;
        }

        var percent = ReadNumber(usage, "remainingPercentage");
        if (percent is null)
        {
            // Порядок фолбэков сохранён из прежней реализации: limit вперёд total.
            var ceiling = ReadNumber(usage, "limit") ?? ReadNumber(usage, "total");
            if (ceiling > 0)
            {
                percent = remaining / ceiling.Value * 100.0;
            }
        }

        numbers = new QuotaNumbers(remaining, percent, total, ReadResetAt(usage));
        return true;
    }

    private static double? ReadNumber(System.Text.Json.JsonElement usage, string property) =>
        usage.TryGetProperty(property, out var element) &&
        element.ValueKind == System.Text.Json.JsonValueKind.Number
            ? element.GetDouble()
            : null;

    /// <summary>
    /// Момент обнуления окна. Нода отдаёт ISO-8601 в UTC (<c>2026-08-01T17:52:38.000Z</c>);
    /// разбираем инвариантной культурой с <see cref="DateTimeStyles.RoundtripKind"/>, иначе
    /// локаль хоста подменит смещение. Мусор в поле — просто отсутствие срока, не ошибка квоты.
    /// </summary>
    private static DateTimeOffset? ReadResetAt(System.Text.Json.JsonElement usage) =>
        usage.TryGetProperty("resetAt", out var element) &&
        element.ValueKind == System.Text.Json.JsonValueKind.String &&
        DateTimeOffset.TryParse(
            element.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
}
