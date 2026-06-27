using System.Threading;
using Microsoft.Extensions.Hosting;
using OpenRussiaRouter.BuildingBlocks.Reasoning;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Collector;

namespace OpenRussiaRouter.WebApp.Services.Enrichment;

/// <summary>
/// Background worker that periodically backfills reasoning capabilities
/// (<c>supports_reasoning</c> / <c>allowed_reasoning_efforts</c> / <c>default_reasoning_effort</c>)
/// on billing models from the 9Router node catalog snapshot maintained by
/// <see cref="CollectorProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// Node catalog data is itself cached by <c>CollectorService</c> (which polls 9Router nodes on
/// its own schedule), so this worker does no HTTP I/O to nodes — it only fans out one gRPC call
/// to Collector and a handful of upserts to Billing. Per project guidance, reasoning levels on
/// 9Router change roughly never, hence the default 1-hour interval.
/// </para>
/// <para>
/// <b>Idempotent &amp; non-clobbering.</b> A model already marked reasoning-capable with a
/// non-empty allowed-efforts list is left untouched — only (a) models not yet marked, or
/// (b) models marked but with an empty efforts list, are patched. This preserves admin-curated
/// overrides while still self-healing catalogue drift.
/// </para>
/// <para>
/// <b>Per-replica.</b> Each WebApp replica runs its own worker; upserts are idempotent by
/// <c>model_id</c>, so concurrent sweeps converge without coordination.
/// </para>
/// </remarks>
public sealed class ReasoningEnrichmentWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceProvider _services;
    private readonly ILogger<ReasoningEnrichmentWorker> _logger;

    public ReasoningEnrichmentWorker(
        IServiceProvider services,
        ILogger<ReasoningEnrichmentWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Reasoning enrichment sweep failed: {Message}. Will retry in {Interval}.",
                    ex.Message, Interval);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingProvider>();
        var collector = scope.ServiceProvider.GetRequiredService<CollectorProvider>();

        var catalog = await collector.ListNodeModelCatalogAsync(ct);
        if (!string.IsNullOrWhiteSpace(catalog.Error))
        {
            _logger.LogDebug(
                "Reasoning enrichment skipped: collector catalog unavailable: {Error}",
                catalog.Error);
            return;
        }

        if (catalog.Entries.Count == 0)
        {
            return;
        }

        var nodeCatalogByUpstream = catalog.Entries
            .Where(static e => !string.IsNullOrWhiteSpace(e.UpstreamId))
            .GroupBy(static e => e.UpstreamId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<ModelInfo> models;
        try
        {
            models = await billing.ListModelsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Reasoning enrichment: ListModels failed: {Message}", ex.Message);
            return;
        }

        var updated = 0;
        foreach (var model in models)
        {
            if (string.IsNullOrWhiteSpace(model.UpstreamModelId))
            {
                continue;
            }
            if (!nodeCatalogByUpstream.TryGetValue(model.UpstreamModelId, out var nodeEntry))
            {
                continue;
            }
            if (!nodeEntry.SupportsReasoning)
            {
                continue;
            }

            var mappedEfforts = NodeReasoningMapper.MapFromNodeOptions(nodeEntry.ThinkingOptions);
            if (mappedEfforts.Count == 0)
            {
                continue;
            }

            ModelInfo? patched = null;
            if (!model.SupportsReasoning)
            {
                patched = model with
                {
                    SupportsReasoning = true,
                    AllowedReasoningEfforts = mappedEfforts,
                    DefaultReasoningEffort = model.DefaultReasoningEffort
                        ?? NodeReasoningMapper.PickDefaultEffort(mappedEfforts)
                };
            }
            else if (model.AllowedReasoningEfforts is null || model.AllowedReasoningEfforts.Count == 0)
            {
                patched = model with
                {
                    AllowedReasoningEfforts = mappedEfforts,
                    DefaultReasoningEffort = model.DefaultReasoningEffort
                        ?? NodeReasoningMapper.PickDefaultEffort(mappedEfforts)
                };
            }

            if (patched is null)
            {
                continue;
            }

            try
            {
                await billing.UpsertModelAsync(patched, ct);
                updated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Reasoning enrichment: upsert failed for {ModelId}: {Message}",
                    model.Id, ex.Message);
            }
        }

        if (updated > 0)
        {
            _logger.LogInformation(
                "Reasoning enrichment sweep updated {Count} model(s) from 9Router catalog.",
                updated);
        }
    }
}
