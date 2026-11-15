using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;
using OpenRussiaRouter.ControlPlane.Application.Contracts;
using OpenRussiaRouter.ControlPlane.Application.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Workers;

public sealed class RouterHealthCheckWorker(
    RouterControlPlaneService controlPlane,
    IRouterNodeHealthProvider healthClient,
    IOptionsMonitor<RouterHealthCheckOptions> options,
    ILogger<RouterHealthCheckWorker> logger)
    : BackgroundService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _consecutiveFailures = new();

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
            return;

        var nodes = await controlPlane.ListNodesForHealthChecksAsync(cancellationToken).ConfigureAwait(false);
        var enabledNodes = nodes.Where(candidate => candidate.Enabled).ToList();

        await Parallel.ForEachAsync(
            enabledNodes,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.CurrentValue.MaxConcurrency,
                CancellationToken = cancellationToken
            },
            async (node, ct) =>
            {
                var result = await healthClient.CheckAsync(node, ct).ConfigureAwait(false);
                var statusToApply = ResolveStatusToApply(node, result);
                await controlPlane.ApplyHealthAsync(
                    node.Id,
                    statusToApply,
                    DateTimeOffset.UtcNow,
                    result.LatencyMs,
                    result.ErrorCode,
                    ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Одиночный неудачный health-check не снимает ноду с маршрутизации: нода получает
    /// Unhealthy/Degraded только после <see cref="RouterHealthCheckOptions.UnhealthyThreshold"/>
    /// подряд идущих неудачных проверок. Успешная проверка сбрасывает счётчик. Пока порог не
    /// достигнут, фиксируется фактический результат проверки (errorCode, last_checked_at), но
    /// статус остаётся Healthy, чтобы роутинг и сбор каталога не прерывались на ~интервал чеков.
    /// </summary>
    private RouterNodeStatus ResolveStatusToApply(RouterNode node, RouterNodeHealthResult result)
    {
        if (result.Status == RouterNodeStatus.Healthy)
        {
            _consecutiveFailures.TryRemove(node.Id, out _);
            return result.Status;
        }

        var failures = _consecutiveFailures.AddOrUpdate(node.Id, 1, static (_, count) => count + 1);
        var threshold = options.CurrentValue.UnhealthyThreshold;
        if (failures >= threshold)
        {
            logger.LogWarning(
                "Router node marked {Status} after {FailureCount} consecutive failed health checks. NodeId: {NodeId}; EndpointUrl: {EndpointUrl}; ErrorCode: {ErrorCode}",
                result.Status, failures, node.Id, node.EndpointUrl, result.ErrorCode);
            return result.Status;
        }

        logger.LogWarning(
            "Router node health check failed ({FailureCount}/{Threshold}); node stays routable. NodeId: {NodeId}; EndpointUrl: {EndpointUrl}; ErrorCode: {ErrorCode}",
            failures, threshold, node.Id, node.EndpointUrl, result.ErrorCode);
        return RouterNodeStatus.Healthy;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Router health check round failed with error: {ErrorMessage}", exception.Message);
            }

            try
            {
                await Task.Delay(options.CurrentValue.Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
