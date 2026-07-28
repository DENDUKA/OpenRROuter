using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;
using OpenRussiaRouter.ControlPlane.Application.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Workers;

public sealed class RouterHealthCheckWorker(
    RouterControlPlaneService controlPlane,
    IRouterNodeHealthProvider healthClient,
    IOptionsMonitor<RouterHealthCheckOptions> options,
    ILogger<RouterHealthCheckWorker> logger)
    : BackgroundService
{

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
                await controlPlane.ApplyHealthAsync(
                    node.Id,
                    result.Status,
                    DateTimeOffset.UtcNow,
                    result.LatencyMs,
                    result.ErrorCode,
                    ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
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
