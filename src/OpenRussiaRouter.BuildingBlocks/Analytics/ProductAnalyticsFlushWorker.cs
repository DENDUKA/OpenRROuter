using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Background worker that drains <see cref="ProductAnalyticsEventQueue"/> into the
/// Postgres analytics database in batches. Failures are logged and the batch is
/// dropped; the worker never stops the host. On shutdown any queued events are
/// flushed best-effort.
/// </summary>
public sealed class ProductAnalyticsFlushWorker : BackgroundService
{
    private readonly ProductAnalyticsEventQueue _queue;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ProductAnalyticsOptions _options;
    private readonly ILogger<ProductAnalyticsFlushWorker> _logger;

    public ProductAnalyticsFlushWorker(
        ProductAnalyticsEventQueue queue,
        NpgsqlDataSource dataSource,
        IOptions<ProductAnalyticsOptions> options,
        ILogger<ProductAnalyticsFlushWorker> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        var batch = new List<QueuedAnalyticsEvent>(_options.BatchSize);

        try
        {
            // Greedy drain: block until at least one event is available, then flush
            // everything currently queued (in BatchSize chunks). This keeps low-volume
            // and rare events (e.g. "user verified", "payment completed") from sitting
            // in memory until BatchSize total events accumulate.
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                await DrainAvailable(reader, batch).ConfigureAwait(false);
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

        // Best-effort flush of anything still buffered on shutdown / channel completion.
        await DrainAvailable(reader, batch).ConfigureAwait(false);
    }

    private async Task DrainAvailable(ChannelReader<QueuedAnalyticsEvent> reader, List<QueuedAnalyticsEvent> batch)
    {
        while (reader.TryRead(out var evt))
        {
            batch.Add(evt);
            if (batch.Count >= _options.BatchSize)
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

    private async Task FlushBatch(List<QueuedAnalyticsEvent> batch, CancellationToken ct)
    {
        try
        {
            var count = batch.Count;
            var eventNames = new string[count];
            var distinctIds = new string[count];
            var occurredAt = new DateTimeOffset[count];
            var dedupKeys = new string?[count];
            var properties = new string[count];
            var schemaVersions = new int[count];
            var environments = new string[count];

            for (var i = 0; i < count; i++)
            {
                var item = batch[i];
                eventNames[i] = item.Event.EventName;
                distinctIds[i] = item.Event.DistinctId;
                occurredAt[i] = item.OccurredAt;
                dedupKeys[i] = item.DedupKey;
                properties[i] = JsonSerializer.Serialize(item.Event.Props);
                schemaVersions[i] = GetSchemaVersion(item.Event.Props);
                environments[i] = GetEnvironment(item.Event.Props);
            }

            const string sql = """
                insert into analytics.events (
                    event_name,
                    distinct_id,
                    occurred_at_utc,
                    dedup_key,
                    properties,
                    schema_version,
                    environment)
                select *
                from unnest(
                    @event_names,
                    @distinct_ids,
                    @occurred_at_utc,
                    @dedup_keys,
                    @properties::jsonb[],
                    @schema_versions,
                    @environments)
                on conflict (event_name, dedup_key) where dedup_key is not null do nothing;
                """;

            await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(new NpgsqlParameter("event_names", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = eventNames });
            command.Parameters.Add(new NpgsqlParameter("distinct_ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = distinctIds });
            command.Parameters.Add(new NpgsqlParameter("occurred_at_utc", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = occurredAt });
            command.Parameters.Add(new NpgsqlParameter("dedup_keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = dedupKeys });
            command.Parameters.Add(new NpgsqlParameter("properties", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = properties });
            command.Parameters.Add(new NpgsqlParameter("schema_versions", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = schemaVersions });
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
                "Failed to flush analytics batch of {BatchSize} events; dropping",
                batch.Count);
        }
    }

    private static int GetSchemaVersion(IReadOnlyDictionary<string, object> props)
    {
        if (props.TryGetValue(AnalyticsEvents.SchemaVersionKey, out var value) &&
            value is not null &&
            int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            return version;
        }

        return 1;
    }

    private static string GetEnvironment(IReadOnlyDictionary<string, object> props)
    {
        if (props.TryGetValue(AnalyticsEvents.EnvironmentKey, out var value) && value is not null)
        {
            return value.ToString() ?? string.Empty;
        }

        return string.Empty;
    }
}
