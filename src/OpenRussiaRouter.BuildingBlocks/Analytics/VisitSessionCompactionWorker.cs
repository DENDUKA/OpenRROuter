using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Runs <see cref="VisitSessionCompactor"/> on a timer: collapses raw page views from
/// <c>analytics.site_visits</c> into <c>analytics.visit_sessions</c> and deletes the source
/// rows, so the visits table stays roughly one session gap deep instead of growing forever.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it runs.</b> Registered by
/// <see cref="SiteVisitServiceCollectionExtensions.AddSiteVisitCompaction"/>, which the
/// BillingService calls — it owns the <c>orr_analytics</c> database, applies its migrations
/// and runs as a single replica. WebApp is replicated, so hosting it there would put N
/// candidates on one compaction; the compactor's advisory lock covers that case anyway
/// (rolling update, a second deployment), but a single owner is the simpler contract.
/// </para>
/// <para>
/// <b>Many short transactions, not one long one.</b> A run makes up to
/// <c>MaxIterationsPerRun</c> passes, each its own transaction inside the compactor, carrying
/// the previous pass's <c>LastVisitorHash</c> forward as the keyset cursor. Wrapping the whole
/// run in one transaction would pin <c>xmin</c> for its entire duration and keep autovacuum
/// away from the very table this worker is emptying.
/// </para>
/// <para>
/// <b>Fail-open</b>, like the rest of <c>Analytics/</c>: a failing pass is logged and retried
/// on the next tick, and never stops the host.
/// </para>
/// </remarks>
public sealed class VisitSessionCompactionWorker : BackgroundService
{
    /// <summary>Stagger after boot: lets migrations finish and avoids a thundering herd.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    /// <summary>Floor for the configured interval; a zero would turn the loop into a spin.</summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    private readonly VisitSessionCompactor _compactor;
    private readonly SiteVisitOptions.CompactionOptions _options;
    private readonly ILogger<VisitSessionCompactionWorker> _logger;

    public VisitSessionCompactionWorker(
        VisitSessionCompactor compactor,
        IOptions<SiteVisitOptions> options,
        ILogger<VisitSessionCompactionWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _compactor = compactor ?? throw new ArgumentNullException(nameof(compactor));
        _options = options.Value.Compaction;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.IntervalMinutes);
        if (interval < MinimumInterval)
        {
            interval = MinimumInterval;
        }

        var maxIterations = _options.MaxIterationsPerRun > 0 ? _options.MaxIterationsPerRun : 1;

        if (!await DelayAsync(StartupDelay, stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CompactAsync(maxIterations, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Visit session compaction run failed: {Message}. Will retry in {Interval}.",
                    ex.Message,
                    interval);
            }

            if (!await DelayAsync(interval, stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private async Task CompactAsync(int maxIterations, CancellationToken ct)
    {
        string? after = null;
        var sessions = 0;
        var deletedVisits = 0;
        var passes = 0;

        for (var i = 0; i < maxIterations && !ct.IsCancellationRequested; i++)
        {
            var result = await _compactor.CompactOnceAsync(DateTimeOffset.UtcNow, after, ct).ConfigureAwait(false);
            passes++;
            sessions += result.Sessions;
            deletedVisits += result.DeletedVisits;

            // Nothing was written: the backlog is drained, the lock is held elsewhere, or the
            // tables are missing. Stopping here can also leave visitors further along the
            // keyset for the next tick when a whole batch happened to be still-open sessions —
            // deliberate, those rows are not going anywhere and a run must stay short.
            if (result.Sessions == 0 || result.LastVisitorHash is null)
            {
                break;
            }

            after = result.LastVisitorHash;
        }

        if (sessions > 0)
        {
            _logger.LogInformation(
                "Visit session compaction: wrote {Sessions} session(s), deleted {Visits} visit row(s) in {Passes} pass(es).",
                sessions,
                deletedVisits,
                passes);
        }
    }

    /// <summary>Waits, returning <c>false</c> when the host is shutting down.</summary>
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
