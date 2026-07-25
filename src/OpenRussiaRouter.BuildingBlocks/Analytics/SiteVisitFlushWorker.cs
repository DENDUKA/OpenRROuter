using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Background worker that drains <see cref="SiteVisitQueue"/> into
/// <c>analytics.site_visits</c> in batches. Same contract as
/// <see cref="ProductAnalyticsFlushWorker"/>: failures are logged and the batch is
/// dropped, the worker never stops the host, and anything still queued is flushed
/// best-effort on shutdown.
/// </summary>
public sealed class SiteVisitFlushWorker : BackgroundService
{
    private readonly SiteVisitQueue _queue;
    private readonly NpgsqlDataSource _dataSource;
    private readonly SiteVisitOptions _options;
    private readonly ILogger<SiteVisitFlushWorker> _logger;

    public SiteVisitFlushWorker(
        SiteVisitQueue queue,
        NpgsqlDataSource dataSource,
        IOptions<SiteVisitOptions> options,
        ILogger<SiteVisitFlushWorker> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        var batchSize = _options.BatchSize > 0 ? _options.BatchSize : 200;
        var batch = new List<SiteVisit>(batchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                await DrainAvailable(reader, batch, batchSize).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
        catch (ChannelClosedException)
        {
            // Queue writer was completed.
        }

        await DrainAvailable(reader, batch, batchSize).ConfigureAwait(false);
    }

    private async Task DrainAvailable(ChannelReader<SiteVisit> reader, List<SiteVisit> batch, int batchSize)
    {
        while (reader.TryRead(out var visit))
        {
            batch.Add(visit);
            if (batch.Count >= batchSize)
            {
                await FlushBatch(batch, CancellationToken.None).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await FlushBatch(batch, CancellationToken.None).ConfigureAwait(false);
            batch.Clear();
        }
    }

    private async Task FlushBatch(List<SiteVisit> batch, CancellationToken ct)
    {
        try
        {
            var count = batch.Count;
            var visitorHashes = new string[count];
            var distinctIds = new string?[count];
            var occurredAt = new DateTimeOffset[count];
            var paths = new string[count];
            var referrerHosts = new string?[count];
            var referrerKinds = new string[count];
            var utmSources = new string?[count];
            var utmMediums = new string?[count];
            var utmCampaigns = new string?[count];
            var deviceClasses = new string[count];
            var isAuthenticated = new bool[count];
            var environments = new string[count];

            for (var i = 0; i < count; i++)
            {
                var visit = batch[i];
                visitorHashes[i] = visit.VisitorHash;
                distinctIds[i] = visit.DistinctId;
                occurredAt[i] = visit.OccurredAt;
                paths[i] = visit.Path;
                referrerHosts[i] = visit.ReferrerHost;
                referrerKinds[i] = visit.ReferrerKind;
                utmSources[i] = visit.UtmSource;
                utmMediums[i] = visit.UtmMedium;
                utmCampaigns[i] = visit.UtmCampaign;
                deviceClasses[i] = visit.DeviceClass;
                isAuthenticated[i] = visit.IsAuthenticated;
                environments[i] = visit.Environment;
            }

            const string sql = """
                insert into analytics.site_visits (
                    visitor_hash,
                    distinct_id,
                    occurred_at_utc,
                    path,
                    referrer_host,
                    referrer_kind,
                    utm_source,
                    utm_medium,
                    utm_campaign,
                    device_class,
                    is_authenticated,
                    environment)
                select *
                from unnest(
                    @visitor_hashes,
                    @distinct_ids,
                    @occurred_at_utc,
                    @paths,
                    @referrer_hosts,
                    @referrer_kinds,
                    @utm_sources,
                    @utm_mediums,
                    @utm_campaigns,
                    @device_classes,
                    @is_authenticated,
                    @environments);
                """;

            await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(new NpgsqlParameter("visitor_hashes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = visitorHashes });
            command.Parameters.Add(new NpgsqlParameter("distinct_ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = distinctIds });
            command.Parameters.Add(new NpgsqlParameter("occurred_at_utc", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = occurredAt });
            command.Parameters.Add(new NpgsqlParameter("paths", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = paths });
            command.Parameters.Add(new NpgsqlParameter("referrer_hosts", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = referrerHosts });
            command.Parameters.Add(new NpgsqlParameter("referrer_kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = referrerKinds });
            command.Parameters.Add(new NpgsqlParameter("utm_sources", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = utmSources });
            command.Parameters.Add(new NpgsqlParameter("utm_mediums", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = utmMediums });
            command.Parameters.Add(new NpgsqlParameter("utm_campaigns", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = utmCampaigns });
            command.Parameters.Add(new NpgsqlParameter("device_classes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = deviceClasses });
            command.Parameters.Add(new NpgsqlParameter("is_authenticated", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isAuthenticated });
            command.Parameters.Add(new NpgsqlParameter("environments", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = environments });

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to flush site visit batch of {BatchSize} rows; dropping",
                batch.Count);
        }
    }
}
