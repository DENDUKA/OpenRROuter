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

        // Resolve visitor_hash from userId: compute distinct_id, then take the most recent
        // visit that carries it — its visitor_hash anchors the anonymous part of the journey.
        if (!string.IsNullOrWhiteSpace(query.UserId))
        {
            try
            {
                resolvedDistinctId = _pseudonymizer.ComputeDistinctId(query.UserId.Trim());
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
        else if (!string.IsNullOrWhiteSpace(query.VisitorHash))
        {
            resolvedVisitorHash = query.VisitorHash.Trim();
        }
        else
        {
            // No filter — caller should pass either. Return empty so the UI can prompt.
            return new SiteVisitQueryResult(Array.Empty<VisitHistoryEntry>(), 0, null, null);
        }

        var total = await CountVisitsAsync(_dataSource, resolvedVisitorHash!, query, ct).ConfigureAwait(false);
        var items = await ListVisitsAsync(_dataSource, resolvedVisitorHash!, query, offset, pageSize, ct)
            .ConfigureAwait(false);

        return new SiteVisitQueryResult(items, total, resolvedVisitorHash, resolvedDistinctId);
    }

    private static async Task<string?> FindVisitorHashByDistinctIdAsync(
        NpgsqlDataSource ds, string distinctId, CancellationToken ct)
    {
        const string sql = """
            select visitor_hash
            from analytics.site_visits
            where distinct_id = :distinctId
            order by occurred_at_utc desc
            limit 1
            """;

        await using var conn = await ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("distinctId", distinctId));

        return (string?)await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> CountVisitsAsync(
        NpgsqlDataSource ds, string visitorHash, SiteVisitQuery query, CancellationToken ct)
    {
        var sql = """
            select count(*)
            from analytics.site_visits
            where visitor_hash = :visitorHash
            """;

        (sql, var parameters) = ApplyTimeWindow(sql, query, BuildBaseParameters(visitorHash));

        await using var conn = await ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is int i ? i : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<VisitHistoryEntry>> ListVisitsAsync(
        NpgsqlDataSource ds, string visitorHash, SiteVisitQuery query, int offset, int pageSize, CancellationToken ct)
    {
        var sql = """
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
            where visitor_hash = :visitorHash
            """;

        var parameters = BuildBaseParameters(visitorHash);
        (sql, parameters) = ApplyTimeWindow(sql, query, parameters);

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

    private static List<NpgsqlParameter> BuildBaseParameters(string visitorHash) =>
        new(1) { new NpgsqlParameter("visitorHash", visitorHash) };

    /// <summary>
    /// Appends <c>:fromUtc</c>/<c>:toUtc</c> predicates to the SQL and adds the matching
    /// parameters when the query specifies a time window. Keeps both count and list queries
    /// in sync — caller passes in the SQL + the parameters accumulated so far.
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
