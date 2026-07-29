using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Outcome of a single compaction pass.
/// </summary>
/// <param name="Sessions">Session rows written (inserted or merged) in this pass.</param>
/// <param name="DeletedVisits">Rows removed from <c>analytics.site_visits</c>; only visits
/// that ended up inside a written session are deleted.</param>
/// <param name="LastVisitorHash">Highest <c>visitor_hash</c> of the batch that was examined,
/// to be passed back as <c>after</c> for the next pass. <c>null</c> when nothing was
/// examined (no candidates, lock not taken, table missing) — the next pass then restarts
/// from the beginning of the keyset.</param>
public sealed record CompactionResult(int Sessions, int DeletedVisits, string? LastVisitorHash)
{
    /// <summary>Nothing was done; the caller should not advance its keyset cursor.</summary>
    public static CompactionResult Empty { get; } = new(0, 0, null);
}

/// <summary>
/// Knobs of one compaction pass. Passed in by the caller (the hosted worker binds them from
/// configuration) so this type stays free of <c>IOptions</c> and of any hosting concern.
/// </summary>
/// <param name="SessionGap">Inactivity that starts a new session; see
/// <see cref="VisitSessionizer.Split"/>.</param>
/// <param name="SafetyLag">Extra margin added to <paramref name="SessionGap"/> when computing
/// the cutoff. It absorbs the flush queue delay and the clock skew between WebApp replicas:
/// <c>occurred_at_utc</c> is stamped by the application, not by the database default.</param>
/// <param name="VisitorBatchSize">How many visitors one pass takes.</param>
/// <param name="MaxPagesPerSession">Upper bound on the stored <c>pages</c> array.</param>
/// <param name="SplitOnUtcDayBoundary">Whether a UTC day change also closes a session.</param>
public sealed record VisitSessionCompactionSettings(
    TimeSpan SessionGap,
    TimeSpan SafetyLag,
    int VisitorBatchSize,
    int MaxPagesPerSession,
    bool SplitOnUtcDayBoundary = true);

/// <summary>
/// Collapses raw page views from <c>analytics.site_visits</c> into
/// <c>analytics.visit_sessions</c> rows and deletes the source rows.
/// </summary>
/// <remarks>
/// One pass is one transaction on one connection, guarded by a transaction-scoped advisory
/// lock, so several hosts (rolling update, a second deployment) cannot compact the same rows
/// twice. Passes are deliberately short: a long transaction would pin <c>xmin</c> and keep
/// autovacuum away from the very table this worker is emptying.
/// </remarks>
public sealed class VisitSessionCompactor
{
    // Advisory lock coordinates as C# literals. hashtext() would be the obvious way to derive
    // them from a name, but its result is not stable across PostgreSQL major versions — during
    // an upgrade the same logical lock would map to two different keys and both compactors
    // would run. "ORR" and "VSC" in ASCII, spelled out so they are greppable.
    private const int LockNamespace = 0x004F_5252;
    private const int LockId = 0x0056_5343;

    private static readonly JsonSerializerOptions PagesJsonOptions = CreatePagesJsonOptions();

    private readonly NpgsqlDataSource _dataSource;
    private readonly VisitSessionCompactionSettings _settings;
    private readonly ILogger<VisitSessionCompactor> _logger;

    private bool _missingTableLogged;

    public VisitSessionCompactor(
        NpgsqlDataSource dataSource,
        VisitSessionCompactionSettings settings,
        ILogger<VisitSessionCompactor> logger)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_settings.SessionGap < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), _settings.SessionGap, "Session gap must not be negative.");
        }

        if (_settings.SafetyLag < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), _settings.SafetyLag, "Safety lag must not be negative.");
        }

        if (_settings.VisitorBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), _settings.VisitorBatchSize, "Visitor batch size must be positive.");
        }

        if (_settings.MaxPagesPerSession < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), _settings.MaxPagesPerSession, "Page limit must not be negative.");
        }
    }

    /// <summary>
    /// Runs one compaction pass.
    /// </summary>
    /// <param name="nowUtc">Current time. The cutoff is
    /// <c>nowUtc - (SessionGap + SafetyLag)</c>; only sessions that ended before it are
    /// written, everything newer is still allowed to grow.</param>
    /// <param name="after">Keyset cursor: only visitors with
    /// <c>visitor_hash &gt; after</c> are considered. <c>null</c> starts from the beginning.
    /// The cursor is what guarantees progress — a visitor whose session never closes would
    /// otherwise sit at the head of every batch forever.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="CompactionResult.Empty"/> when the advisory lock was not granted,
    /// when the tables are missing, or when the batch is empty.</returns>
    public async Task<CompactionResult> CompactOnceAsync(DateTimeOffset nowUtc, string? after, CancellationToken ct)
    {
        var cutoff = nowUtc - (_settings.SessionGap + _settings.SafetyLag);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Transaction-scoped on purpose: pg_advisory_lock() is session-scoped, and sessions
        // come from the Npgsql pool — a missed unlock would leak the lock into whatever code
        // borrows that physical connection next. _xact_ is released by COMMIT/ROLLBACK.
        if (!await TryAcquireLockAsync(connection, transaction, ct).ConfigureAwait(false))
        {
            _logger.LogDebug("Visit session compaction lock is held elsewhere; skipping this pass");
            return CompactionResult.Empty;
        }

        if (!await TablesExistAsync(connection, transaction, ct).ConfigureAwait(false))
        {
            return CompactionResult.Empty;
        }

        var visitorHashes = await LoadCandidatesAsync(connection, transaction, cutoff, after, ct).ConfigureAwait(false);
        if (visitorHashes.Count == 0)
        {
            return CompactionResult.Empty;
        }

        var visits = await LoadVisitsAsync(connection, transaction, visitorHashes, ct).ConfigureAwait(false);
        var (rows, visitIds) = BuildClosedSessions(visits, cutoff);

        var deleted = 0;
        if (rows.Count > 0)
        {
            await InsertSessionsAsync(connection, transaction, rows, ct).ConfigureAwait(false);
            deleted = await DeleteVisitsAsync(connection, transaction, visitIds, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Compacted {Sessions} visit sessions from {Visitors} visitors, deleted {Visits} visits",
            rows.Count,
            visitorHashes.Count,
            deleted);

        return new CompactionResult(rows.Count, deleted, visitorHashes[^1]);
    }

    private static async Task<bool> TryAcquireLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        const string sql = "select pg_try_advisory_xact_lock(@ns, @id);";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("ns", NpgsqlDbType.Integer) { Value = LockNamespace });
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = LockId });

        var granted = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return granted is true;
    }

    private async Task<bool> TablesExistAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        // to_regclass returns null instead of raising when the relation is absent, which is
        // what makes this a guard and not another way to throw. Fail-open, like the rest of
        // Analytics/: an analytics database that is a migration behind must not turn into an
        // exception on every tick of the worker.
        const string sql = """
            select to_regclass('analytics.site_visits') is not null,
                   to_regclass('analytics.visit_sessions') is not null;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var visitsExist = false;
        var sessionsExist = false;
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            visitsExist = reader.GetBoolean(0);
            sessionsExist = reader.GetBoolean(1);
        }

        if (visitsExist && sessionsExist)
        {
            return true;
        }

        if (!_missingTableLogged)
        {
            _missingTableLogged = true;
            _logger.LogWarning(
                "Visit session compaction is disabled: analytics.site_visits exists = {VisitsExist}, "
                + "analytics.visit_sessions exists = {SessionsExist}. Apply the analytics migrations.",
                visitsExist,
                sessionsExist);
        }

        return false;
    }

    private async Task<List<string>> LoadCandidatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset cutoff,
        string? after,
        CancellationToken ct)
    {
        // Candidates are picked by the cutoff, but their rows are read in full below: a
        // visitor is worth looking at only if they have something old enough to close.
        const string sql = """
            select distinct visitor_hash
            from analytics.site_visits
            where occurred_at_utc < @cutoff
              and visitor_hash > @after
            order by visitor_hash
            limit @batch_size;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = cutoff });
        command.Parameters.Add(new NpgsqlParameter("after", NpgsqlDbType.Text) { Value = after ?? string.Empty });
        command.Parameters.Add(new NpgsqlParameter("batch_size", NpgsqlDbType.Integer) { Value = _settings.VisitorBatchSize });

        var hashes = new List<string>(_settings.VisitorBatchSize);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            hashes.Add(reader.GetString(0));
        }

        return hashes;
    }

    private static async Task<List<CompactableVisit>> LoadVisitsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        List<string> visitorHashes,
        CancellationToken ct)
    {
        // Deliberately unfiltered by the cutoff: the gap between the last old row and the
        // first fresh one is what decides whether the trailing session is closed. Filtering
        // here would chop live sessions into fragments.
        const string sql = """
            select id,
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
                   environment
            from analytics.site_visits
            where visitor_hash = any(@hashes)
            order by visitor_hash, occurred_at_utc, id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("hashes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = visitorHashes.ToArray() });

        var visits = new List<CompactableVisit>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            visits.Add(new CompactableVisit(
                Id: reader.GetInt64(0),
                VisitorHash: reader.GetString(1),
                DistinctId: reader.IsDBNull(2) ? null : reader.GetString(2),
                OccurredAt: reader.GetFieldValue<DateTimeOffset>(3),
                Path: reader.GetString(4),
                ReferrerHost: reader.IsDBNull(5) ? null : reader.GetString(5),
                ReferrerKind: reader.GetString(6),
                UtmSource: reader.IsDBNull(7) ? null : reader.GetString(7),
                UtmMedium: reader.IsDBNull(8) ? null : reader.GetString(8),
                UtmCampaign: reader.IsDBNull(9) ? null : reader.GetString(9),
                DeviceClass: reader.GetString(10),
                IsAuthenticated: reader.GetBoolean(11),
                Environment: reader.GetString(12)));
        }

        return visits;
    }

    private (List<VisitSessionRow> Rows, List<long> VisitIds) BuildClosedSessions(
        List<CompactableVisit> visits,
        DateTimeOffset cutoff)
    {
        var rows = new List<VisitSessionRow>();
        var visitIds = new List<long>();

        foreach (var visitor in visits.GroupBy(static v => v.VisitorHash, StringComparer.Ordinal))
        {
            var slices = VisitSessionizer.Split(visitor, _settings.SessionGap, _settings.SplitOnUtcDayBoundary);
            foreach (var slice in slices)
            {
                // A session may still grow while its tail is newer than the cutoff, so it is
                // left in site_visits untouched and picked up by a later pass.
                if (slice[^1].OccurredAt >= cutoff)
                {
                    continue;
                }

                rows.Add(VisitSessionAggregator.Aggregate(slice, _settings.MaxPagesPerSession));
                foreach (var visit in slice)
                {
                    visitIds.Add(visit.Id);
                }
            }
        }

        return (rows, visitIds);
    }

    private async Task InsertSessionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        List<VisitSessionRow> rows,
        CancellationToken ct)
    {
        var count = rows.Count;
        var visitorHashes = new string[count];
        var distinctIds = new string?[count];
        var startedAt = new DateTimeOffset[count];
        var endedAt = new DateTimeOffset[count];
        var durationSeconds = new int[count];
        var pageViewCounts = new int[count];
        var entryPaths = new string[count];
        var exitPaths = new string[count];
        var referrerHosts = new string?[count];
        var referrerKinds = new string[count];
        var utmSources = new string?[count];
        var utmMediums = new string?[count];
        var utmCampaigns = new string?[count];
        var deviceClasses = new string[count];
        var isAuthenticated = new bool[count];
        var pages = new string[count];
        var environments = new string[count];

        for (var i = 0; i < count; i++)
        {
            var row = rows[i];
            visitorHashes[i] = row.VisitorHash;
            distinctIds[i] = row.DistinctId;
            startedAt[i] = row.StartedAt;
            endedAt[i] = row.EndedAt;
            durationSeconds[i] = row.DurationSeconds;
            pageViewCounts[i] = row.PageViewCount;
            entryPaths[i] = row.EntryPath;
            exitPaths[i] = row.ExitPath;
            referrerHosts[i] = row.ReferrerHost;
            referrerKinds[i] = row.ReferrerKind;
            utmSources[i] = row.UtmSource;
            utmMediums[i] = row.UtmMedium;
            utmCampaigns[i] = row.UtmCampaign;
            deviceClasses[i] = row.DeviceClass;
            isAuthenticated[i] = row.IsAuthenticated;
            pages[i] = JsonSerializer.Serialize(row.Pages, PagesJsonOptions);
            environments[i] = row.Environment;
        }

        // Merge, not `do nothing`: a visit flushed later than SafetyLag lands in a session
        // that is already written. With `do nothing` the row would be silently dropped while
        // the delete below still removed it — the page view would vanish from both tables.
        const string sql = """
            insert into analytics.visit_sessions as vs (
                visitor_hash,
                distinct_id,
                started_at_utc,
                ended_at_utc,
                duration_seconds,
                page_view_count,
                entry_path,
                exit_path,
                referrer_host,
                referrer_kind,
                utm_source,
                utm_medium,
                utm_campaign,
                device_class,
                is_authenticated,
                pages,
                environment)
            select *
            from unnest(
                @visitor_hashes,
                @distinct_ids,
                @started_at_utc,
                @ended_at_utc,
                @duration_seconds,
                @page_view_counts,
                @entry_paths,
                @exit_paths,
                @referrer_hosts,
                @referrer_kinds,
                @utm_sources,
                @utm_mediums,
                @utm_campaigns,
                @device_classes,
                @is_authenticated,
                @pages,
                @environments)
            on conflict (visitor_hash, started_at_utc) do update set
                ended_at_utc     = greatest(vs.ended_at_utc, excluded.ended_at_utc),
                duration_seconds = extract(epoch from (greatest(vs.ended_at_utc, excluded.ended_at_utc) - vs.started_at_utc))::int,
                page_view_count  = vs.page_view_count + excluded.page_view_count,
                exit_path        = case when excluded.ended_at_utc >= vs.ended_at_utc then excluded.exit_path else vs.exit_path end,
                distinct_id      = coalesce(excluded.distinct_id, vs.distinct_id),
                utm_source       = coalesce(vs.utm_source, excluded.utm_source),
                utm_medium       = coalesce(vs.utm_medium, excluded.utm_medium),
                utm_campaign     = coalesce(vs.utm_campaign, excluded.utm_campaign),
                is_authenticated = vs.is_authenticated or excluded.is_authenticated,
                pages            = case when jsonb_array_length(vs.pages) >= @max_pages
                                        then vs.pages
                                        else vs.pages || excluded.pages end;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("visitor_hashes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = visitorHashes });
        command.Parameters.Add(new NpgsqlParameter("distinct_ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = distinctIds });
        command.Parameters.Add(new NpgsqlParameter("started_at_utc", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = startedAt });
        command.Parameters.Add(new NpgsqlParameter("ended_at_utc", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = endedAt });
        command.Parameters.Add(new NpgsqlParameter("duration_seconds", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = durationSeconds });
        command.Parameters.Add(new NpgsqlParameter("page_view_counts", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = pageViewCounts });
        command.Parameters.Add(new NpgsqlParameter("entry_paths", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = entryPaths });
        command.Parameters.Add(new NpgsqlParameter("exit_paths", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = exitPaths });
        command.Parameters.Add(new NpgsqlParameter("referrer_hosts", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = referrerHosts });
        command.Parameters.Add(new NpgsqlParameter("referrer_kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = referrerKinds });
        command.Parameters.Add(new NpgsqlParameter("utm_sources", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = utmSources });
        command.Parameters.Add(new NpgsqlParameter("utm_mediums", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = utmMediums });
        command.Parameters.Add(new NpgsqlParameter("utm_campaigns", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = utmCampaigns });
        command.Parameters.Add(new NpgsqlParameter("device_classes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = deviceClasses });
        command.Parameters.Add(new NpgsqlParameter("is_authenticated", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isAuthenticated });
        command.Parameters.Add(new NpgsqlParameter("pages", NpgsqlDbType.Array | NpgsqlDbType.Jsonb) { Value = pages });
        command.Parameters.Add(new NpgsqlParameter("environments", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = environments });
        command.Parameters.Add(new NpgsqlParameter("max_pages", NpgsqlDbType.Integer) { Value = _settings.MaxPagesPerSession });

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> DeleteVisitsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        List<long> visitIds,
        CancellationToken ct)
    {
        // Only the ids that went into the rows written above: everything else stays, which is
        // what keeps a live session intact when part of it was already compacted.
        const string sql = "delete from analytics.site_visits where id = any(@ids);";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = visitIds.ToArray() });

        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static JsonSerializerOptions CreatePagesJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        options.Converters.Add(new UtcTimestampConverter());
        return options;
    }

    /// <summary>
    /// Writes page timestamps as <c>2026-07-27T10:00:03.123Z</c> — the shape documented on
    /// the <c>pages</c> column. Millisecond precision, trailing zeros trimmed: the array is
    /// stored per session and never searched inside, so it is kept small.
    /// </summary>
    private sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFF'Z'", CultureInfo.InvariantCulture));
        }
    }
}
