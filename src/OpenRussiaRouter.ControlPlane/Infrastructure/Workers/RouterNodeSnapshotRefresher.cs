using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Persistence;
using OpenRussiaRouter.ControlPlane.Application.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Workers;

public sealed class RouterNodeSnapshotRefresher(
    IControlPlaneRepository repository,
    RouterNodeSnapshotStore snapshotStore,
    IOptionsMonitor<RouterNodeSnapshotOptions> options,
    ILogger<RouterNodeSnapshotRefresher> logger)
    : BackgroundService
{
    private const int PageSize = 1000;

    public async Task RunOnceAsync(CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
            return;

        var nodes = new List<RouterNode>();
        string? afterName = null;
        while (true)
        {
            var page = await repository.ListNodesAsync(PageSize, afterName, ct).ConfigureAwait(false);
            nodes.AddRange(page.Items);
            if (!page.HasMore)
                break;

            afterName = nodes[^1].Name;
        }

        snapshotStore.ReplaceAll(nodes);
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
                logger.LogWarning(exception, "Router-node metadata snapshot refresh failed with error: {ErrorMessage}", exception.Message);
            }

            try
            {
                await Task.Delay(options.CurrentValue.RefreshInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
