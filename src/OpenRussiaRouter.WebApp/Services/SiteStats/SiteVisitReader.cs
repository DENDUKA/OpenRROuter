using Microsoft.Extensions.Logging;
using Npgsql;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.WebApp.Services.SiteStats;

/// <summary>
/// Reads visit history from <c>analytics.site_visits</c> for the admin UI. The data is
/// written by <see cref="SiteVisitFlushWorker"/> via <see cref="SiteVisitTrackingMiddleware"/>;
/// this reader is the read side of the same table. WebApp already owns the
/// <c>orr_analytics</c> Npgsql data source for the write side, so we reuse it.
/// </summary>
/// <remarks>
/// When analytics is not configured (no <c>NpgsqlDataSource</c> in DI — e.g. dev/test
/// without the sink), <see cref="IsEnabled"/> is <c>false</c> and queries return an empty
/// result. The page is expected to surface this to the admin instead of crashing.
/// </remarks>
public sealed class SiteVisitReader
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly UserPseudonymizer? _pseudonymizer;
    private readonly ILogger<SiteVisitReader> _logger;

    public SiteVisitReader(
        NpgsqlDataSource? dataSource,
        UserPseudonymizer? pseudonymizer,
        ILogger<SiteVisitReader> logger)
    {
        _dataSource = dataSource;
        _pseudonymizer = pseudonymizer;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary><c>true</c> when the analytics sink is wired (data source and pseudonymizer
    /// are both available). <c>false</c> in dev/test without the sink.</summary>
    public bool IsEnabled => _dataSource is not null && _pseudonymizer is not null;

    /// <summary>
    /// Runs the query. See <see cref="SiteVisitQuery"/> for the matching logic. Never throws
    /// on a disabled sink — returns an empty result so callers can render a single uniform
    /// "analytics disabled" banner in the UI.
    /// </summary>
    public async Task<SiteVisitQueryResult> QueryAsync(SiteVisitQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_dataSource is null || _pseudonymizer is null)
        {
            return new SiteVisitQueryResult(Array.Empty<VisitHistoryEntry>(), 0, null, null);
        }

        var pageSize = query.PageSize > 0 ? query.PageSize : 50;
        var pageNumber = Math.Max(0, query.PageNumber);
        var offset = pageNumber * pageSize;

        string? resolvedVisitorHash = null;
        string? resolvedDistinctId = null;
        var hasUserFilter = !string.IsNullOrWhiteSpace(query.UserId);
        var hasHashFilter = !string.IsNullOrWhiteSpace(query.VisitorHash);

        // Resolve visitor_hash from userId: compute distinct_id, then take the most recent
        // visit (or, once it has been compacted away, session) that carries it — its
        // visitor_hash anchors the anonymous part of the journey.
        if (hasUserFilter)
        {
            try
            {
                resolvedDistinctId = _pseudonymizer.ComputeDistinctId(query.UserId!.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to compute distinct_id for visitor-history query");
                return new SiteVisitQueryResult(Array.Empty<VisitHistoryEntry>(), 0, null, null);
            }

            resolvedVisitorHash = await FindVisitorHashByDistinctIdAsync(_dataSource, resolvedDistinctId, ct)
                .ConfigureAwait(false);

            // No signed-in visit for this user — nothing to show yet.
            if (string.IsNullOrEmpty(resolvedVisitorHash))
            {
                return new SiteVisitQueryResult(
                    Array.Empty<VisitHistoryEntry>(), 0, null, resolvedDistinctId);
            }
        }
        else if (hasHashFilter)
        {
            resolvedVisitorHash = query.VisitorHash!.Trim();
        }
        // No filter at all → fall through to "latest visits across everyone" mode, same as
        // how /admin/topups behaves. This is the default landing view so the page is not
        // empty when an admin opens it without parameters.

        string? filterColumn = (hasUserFilter || hasHashFilter) ? "visitor_hash" : null;

        var total = await CountVisitsAsync(_dataSource, filterColumn, resolvedVisitorHash, query, ct)
            .ConfigureAwait(false);
        var items = await ListVisitsAsync(_dataSource, filterColumn, resolvedVisitorHash, query, offset, pageSize, ct)
            .ConfigureAwait(false);

        return new SiteVisitQueryResult(items, total, resolvedVisitorHash, resolvedDistinctId);
    }

    /// <summary>
    /// Resolves the visitor behind a <c>distinct_id</c>, newest first. Looks in
    /// <c>analytics.site_visits</c> and, when that finds nothing, falls back to
    /// <c>analytics.visit_sessions</c>.
    /// </summary>
    /// <remarks>
    /// The fallback is what keeps <c>/admin/visitors?userId=</c> working at all: the compaction
    /// worker deletes raw visits once their session is closed, so <c>site_visits</c> holds only
    /// the last ~35 minutes of traffic and every older user would otherwise resolve to nothing.
    /// The raw table is still queried first — it is the only place a still-open session lives,
    /// and its <c>visitor_hash</c> is the freshest one available.
    /// </remarks>
    private async Task<string?> FindVisitorHashByDistinctIdAsync(
        NpgsqlDataSource ds, string distinctId, CancellationToken ct)
    {
        const string visitsSql = """
            select visitor_hash
            from analytics.site_visits
            where distinct_id = :distinctId
            order by occurred_at_utc desc
            limit 1
            """;

        const string sessionsSql = """
            select visitor_hash
            from analytics.visit_sessions
            where distinct_id = :distinctId
            order by started_at_utc desc
            limit 1
            """;

        var fromVisits = await QueryVisitorHashAsync(ds, visitsSql, distinctId, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(fromVisits))
        {
            return fromVisits;
        }

        try
        {
            return await QueryVisitorHashAsync(ds, sessionsSql, distinctId, ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Analytics is fail-open: a database that is one migration behind must degrade to
            // "no visitor found" (exactly what the pre-compaction reader returned) instead of
            // failing the admin page. A to_regclass pre-check would cost an extra round trip on
            // every lookup to guard a state that ends the moment the migration is applied;
            // catching the one error code that means "table absent" pays only in that state and
            // leaves every other Postgres failure visible.
            _logger.LogWarning(ex,
                "analytics.visit_sessions is missing, visitor resolution is limited to the "
                + "uncompacted tail of analytics.site_visits. Apply the analytics migrations.");
            return null;
        }
    }

    private static async Task<string?> QueryVisitorHashAsync(
        NpgsqlDataSource ds, string sql, string distinctId, CancellationToken ct)
    {
        await using var conn = await ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("distinctId", distinctId));

        return (string?)await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts visits. When <paramref name="filterColumn"/> is <c>"visitor_hash"</c>, scopes
    /// the count to that visitor; when <c>null</c> (no filter), counts across everyone —
    /// used for the default landing view of the page.
    /// </summary>
    private static async Task<int> CountVisitsAsync(
        NpgsqlDataSource ds, string? filterColumn, string? filterValue, SiteVisitQuery query, CancellationToken ct)
    {
        var sql = "select count(*) from analytics.site_visits where 1=1";
        (sql, var parameters) = ApplyTimeWindow(sql, query, new List<NpgsqlParameter>());
        if (filterColumn is not null && !string.IsNullOrEmpty(filterValue))
        {
            // Hard-coded column name: filterColumn is an internal flag, never user input.
            // Using string interpolation for a SQL identifier here would be a latent
            // injection vector if a future caller passed something through.
            if (filterColumn != "visitor_hash")
            {
                throw new InvalidOperationException(
                    "Unsupported filter column: " + filterColumn);
            }
            sql += " and visitor_hash = :visitor_hash";
            parameters.Add(new NpgsqlParameter("visitor_hash", filterValue));
        }

        await using var conn = await ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) cmd.Parameters.Add(p);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is int i ? i : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Lists visits. When <paramref name="filterColumn"/> is <c>"visitor_hash"</c>, scopes
    /// the rows to that visitor; when <c>null</c>, returns the latest visits across all
    /// visitors — same pattern as <c>/admin/topups</c>. Always ordered newest-first.
    /// </summary>
    private static async Task<IReadOnlyList<VisitHistoryEntry>> ListVisitsAsync(
        NpgsqlDataSource ds, string? filterColumn, string? filterValue, SiteVisitQuery query, int offset, int pageSize, CancellationToken ct)
    {
        var sql = """
            select id, visitor_hash, distinct_id, occurred_at_utc, path,
                   referrer_host, referrer_kind, utm_source, utm_medium, utm_campaign,
                   device_class, is_authenticated, environment
            from analytics.site_visits
            where 1=1
            """;
        var parameters = new List<NpgsqlParameter>();
        (sql, parameters) = ApplyTimeWindow(sql, query, parameters);
        if (filterColumn is not null && !string.IsNullOrEmpty(filterValue))
        {
            // Same whitelist guard as in CountVisitsAsync.
            if (filterColumn != "visitor_hash")
            {
                throw new InvalidOperationException(
                    "Unsupported filter column: " + filterColumn);
            }
            sql += " and visitor_hash = :visitor_hash";
            parameters.Add(new NpgsqlParameter("visitor_hash", filterValue));
        }

        sql += " order by occurred_at_utc desc limit :limit offset :offset";
        parameters.Add(new NpgsqlParameter("limit", pageSize));
        parameters.Add(new NpgsqlParameter("offset", offset));

        await using var conn = await ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        var items = new List<VisitHistoryEntry>(pageSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            items.Add(new VisitHistoryEntry(
                Id: reader.GetInt64(reader.GetOrdinal("id")),
                VisitorHash: reader.GetString(reader.GetOrdinal("visitor_hash")),
                DistinctId: reader.IsDBNull(reader.GetOrdinal("distinct_id"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("distinct_id")),
                OccurredAtUtc: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("occurred_at_utc")),
                Path: reader.GetString(reader.GetOrdinal("path")),
                ReferrerHost: reader.IsDBNull(reader.GetOrdinal("referrer_host"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("referrer_host")),
                ReferrerKind: reader.GetString(reader.GetOrdinal("referrer_kind")),
                UtmSource: reader.IsDBNull(reader.GetOrdinal("utm_source"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("utm_source")),
                UtmMedium: reader.IsDBNull(reader.GetOrdinal("utm_medium"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("utm_medium")),
                UtmCampaign: reader.IsDBNull(reader.GetOrdinal("utm_campaign"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("utm_campaign")),
                DeviceClass: reader.GetString(reader.GetOrdinal("device_class")),
                IsAuthenticated: reader.GetBoolean(reader.GetOrdinal("is_authenticated")),
                Environment: reader.GetString(reader.GetOrdinal("environment"))));
        }

        return items;
    }

    /// <summary>
    /// Appends <c>:fromUtc</c>/<c>:toUtc</c> predicates to the SQL and adds the matching
    /// parameters when the query specifies a time window. Keeps count, list and "latest
    /// across everyone" queries in sync — caller passes in the SQL + the parameters
    /// accumulated so far. The base SQL is assumed to end with a <c>where 1=1</c> anchor
    /// so each appended clause can start with <c>and</c>.
    /// </summary>
    private static (string Sql, List<NpgsqlParameter> Parameters) ApplyTimeWindow(
        string sql, SiteVisitQuery query, List<NpgsqlParameter> parameters)
    {
        if (query.FromUtc.HasValue)
        {
            sql += " and occurred_at_utc >= :fromUtc";
            parameters.Add(new NpgsqlParameter("fromUtc", query.FromUtc.Value));
        }

        if (query.ToUtc.HasValue)
        {
            sql += " and occurred_at_utc <= :toUtc";
            parameters.Add(new NpgsqlParameter("toUtc", query.ToUtc.Value));
        }

        return (sql, parameters);
    }
}
