using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.CollectorService.Application.Billing;

public sealed class GrpcBillingModelCatalogProvider(
    BillingGrpc.BillingServiceClient client,
    IOptionsMonitor<CollectorOptions> options,
    TimeProvider timeProvider,
    ILogger<GrpcBillingModelCatalogProvider> logger) : IBillingModelCatalogProvider
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private IReadOnlyList<BillingCatalogModel> cachedModels = [];
    private DateTimeOffset? cachedAtUtc;

    public async Task<IReadOnlyList<BillingCatalogModel>> ListModels(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (IsCacheFresh(now))
            return cachedModels;

        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = timeProvider.GetUtcNow();
            if (IsCacheFresh(now))
                return cachedModels;

            var response = await client.ListModelsAsync(
                new ListModelsRequest(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Выключенные модели тоже нужны: без них выключенная у нас модель неотличима
            // от модели, которой в каталоге нет вовсе. Фильтр по Enabled стоит у потребителей.
            var models = response.Models
                .Select(model => new BillingCatalogModel(
                    model.Id,
                    string.IsNullOrWhiteSpace(model.UpstreamModelId) ? model.Id : model.UpstreamModelId,
                    model.Enabled))
                .Where(model => !string.IsNullOrWhiteSpace(model.ModelId) &&
                                !string.IsNullOrWhiteSpace(model.RouterModelId))
                .DistinctBy(model => model.ModelId, StringComparer.Ordinal)
                .ToList();

            cachedModels = models;
            cachedAtUtc = now;
            return cachedModels;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded ||
                                      !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to load billing model catalog from BillingService");
            return cachedModels;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to load billing model catalog from BillingService");
            return cachedModels;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private bool IsCacheFresh(DateTimeOffset now)
    {
        return cachedAtUtc is { } cachedAt &&
               now - cachedAt < options.CurrentValue.ActiveModelsRefreshInterval;
    }
}
