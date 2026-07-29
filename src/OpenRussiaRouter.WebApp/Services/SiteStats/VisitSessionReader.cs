using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.WebApp.Services.SiteStats;

/// <summary>
/// Reads collapsed visitor sessions from <c>analytics.visit_sessions</c> for the admin UI.
/// The rows are written by the compaction worker in BillingService, which folds the raw
/// <c>analytics.site_visits</c> page views into one row per session; this reader is the read
/// side of that table and reuses the <c>orr_analytics</c> data source WebApp already owns for
/// the visit write path.
/// </summary>
/// <remarks>
/// Mirrors <see cref="SiteVisitReader"/> deliberately, including the disabled mode: when
/// analytics is not configured (no <c>NpgsqlDataSource</c> in DI — e.g. dev/test without the
/// sink), <see cref="IsEnabled"/> is <c>false</c> and queries return an empty result instead
/// of throwing, so the page can render one uniform banner.
/// </remarks>
public sealed class VisitSessionReader
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly UserPseudonymizer? _pseudonymizer;
    private readonly ILogger<VisitSessionReader> _logger;

    /// <summary>
    /// Aggregate columns of a session row. <c>pages</c> is not here on purpose: it is the one
    /// unbounded column in the table and the listing never renders it.
    /// </summary>
    private const string SessionColumns = """
        id, visitor_hash, distinct_id, started_at_utc, ended_at_utc, duration_seconds,
        page_view_count, entry_path, exit_path, referrer_host, referrer_kind,
        utm_source, utm_medium, utm_campaign, device_class, is_authenticated, environment
        """;

    public VisitSessionReader(
        NpgsqlDataSource? dataSource,
        UserPseudonymizer? pseudonymizer,
        ILogger<VisitSessionReader> logger)
    {
        _dataSource = dataSource;
        _pseudonymizer = pseudonymizer;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary><c>true</c> when the analytics sink is wired (data source and pseudonymizer
    /// are both available). <c>false</c> in dev/test without the sink.</summary>
    public bool IsEnabled => _dataSource is not null && _pseudonymizer is not null;

    /// <summary>
    /// Runs the session query. See <see cref="VisitSessionQuery"/> for the matching logic.
    /// Never throws on a disabled sink — returns an empty result.
    /// </summary>
    public async Task<VisitSessionQueryResult> QueryAsync(VisitSessionQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_dataSource is null || _pseudonymizer is null)
        {
            return new VisitSessionQueryResult(Array.Empty<VisitSessionEntry>(), 0, null, null);
        }

        var pageSize = query.PageSize > 0 ? query.PageSize : 50;
        var pageNumber = Math.Max(0, query.PageNumber);
        var offset = pageNumber * pageSize;

        string? resolvedVisitorHash = null;
        string? resolvedDistinctId = null;
        var hasUserFilter = !string.IsNullOrWhiteSpace(query.UserId);
        var hasHashFilter = !string.IsNullOrWhiteSpace(query.VisitorHash);

        // Resolve visitor_hash from userId: compute distinct_id, then take the most recent
        // session that carries it — its visitor_hash anchors the anonymous part of the journey.
        if (hasUserFilter)
        {
            try
            {
                resolvedDistinctId = _pseudonymizer.ComputeDistinctId(query.UserId!.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compute distinct_id for visit-session query");
                return new VisitSessionQueryResult(Array.Empty<VisitSessionEntry>(), 0, null, null);
            }

            resolvedVisitorHash = await FindVisitorHashByDistinctIdAsync(_dataSource, resolvedDistinctId, ct)
                .ConfigureAwait(false);

            // No signed-in session for this user — nothing to show yet.
            if (string.IsNullOrEmpty(resolvedVisitorHash))
            {
                return new VisitSessionQueryResult(
                    Array.Empty<VisitSessionEntry>(), 0, null, resolvedDistinctId);
            }
        }
        else if (hasHashFilter)
        {
            resolvedVisitorHash = query.VisitorHash!.Trim();
        }
        // No filter at all → "latest sessions across everyone", the default landing view.

        var total = await CountSessionsAsync(_dataSource, resolvedVisitorHash, query, ct).ConfigureAwait(false);
        var items = await ListSessionsAsync(_dataSource, resolvedVisitorHash, query, offset, pageSize, ct)
            .ConfigureAwait(false);

        return new VisitSessionQueryResult(items, total, resolvedVisitorHash, resolvedDistinctId);
    }

    /// <summary>
    /// Loads one session together with its parsed page list. Returns <c>null</c> when the id
    /// does not exist (the caller is expected to answer <c>404</c>) and when the sink is
    /// disabled — the same "empty, not exception" contract as <see cref="QueryAsync"/>.
    /// </summary>
    public async Task<VisitSessionDetails?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        if (_dataSource is null || _pseudonymizer is null)
        {
            return null;
        }

        var sql = $"""
            select {SessionColumns}, pages
            from analytics.visit_sessions
            where id = :id
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("id", id));

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var session = MapSession(reader);
        var pagesJson = reader.IsDBNull(reader.GetOrdinal("pages"))
            ? null
            : reader.GetString(reader.GetOrdinal("pages"));

        return new VisitSessionDetails(session, ParsePages(pagesJson, id));
    }

    /// <summary>
    /// Deserializes the <c>pages</c> jsonb array written by the compactor
    /// (<c>[{"t":"2026-07-27T10:00:03Z","p":"/models"}]</c>). A row we cannot parse degrades
    /// to an empty page list with a warning rather than failing the whole details page —
    /// the session header stays useful either way.
    /// </summary>
    internal IReadOnlyList<VisitSessionPage> ParsePages(string? json, long sessionId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<VisitSessionPage>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<VisitSessionPage>>(json)
                ?? (IReadOnlyList<VisitSessionPage>)Array.Empty<VisitSessionPage>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse pages jsonb of visit session {SessionId}", sessionId);
            return Array.Empty<VisitSessionPage>();
        }
    }

    private static async Task<string?> FindVisitorHashByDistinctIdAsync(
        NpgsqlDataSource ds, string distinctId, CancellationToken ct)
    {
        const string sql = """
            select visitor_hash
            from analytics.visit_sessions
            where distinct_id = :distinctId
            order by started_at_utc desc
            limit 1
            """;

        await using var conn = await ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("distinctId", distinctId));

        return (string?)await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts sessions under the same predicates the listing uses, so the pager matches the
    /// rows. <paramref name="visitorHash"/> is <c>null</c> for the unfiltered landing view.
    /// </summary>
    private static async Task<int> CountSessionsAsync(
        NpgsqlDataSource ds, string? visitorHash, VisitSessionQuery query, CancellationToken ct)
    {
        var sql = "select count(*) from analytics.visit_sessions where 1=1";
        (sql, var parameters) = ApplyFilters(sql, query, visitorHash, new List<NpgsqlParameter>());

        await using var conn = await ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is long l
            ? (int)l
            : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Lists sessions newest-first. Selects the aggregate columns only — <c>pages</c> stays
    /// out of the listing query and is fetched per session by <see cref="GetByIdAsync"/>.
    /// </summary>
    private static async Task<IReadOnlyList<VisitSessionEntry>> ListSessionsAsync(
        NpgsqlDataSource ds, string? visitorHash, VisitSessionQuery query, int offset, int pageSize, CancellationToken ct)
    {
        var sql = $"""
            select {SessionColumns}
            from analytics.visit_sessions
            where 1=1
            """;
        (sql, var parameters) = ApplyFilters(sql, query, visitorHash, new List<NpgsqlParameter>());

        sql += " order by started_at_utc desc limit :limit offset :offset";
        parameters.Add(new NpgsqlParameter("limit", pageSize));
        parameters.Add(new NpgsqlParameter("offset", offset));

        await using var conn = await ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        var items = new List<VisitSessionEntry>(pageSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            items.Add(MapSession(reader));
        }

        return items;
    }

    /// <summary>
    /// Appends the visitor, time-window and bot predicates and the matching parameters.
    /// Shared by count and listing so the two never disagree; the base SQL is assumed to end
    /// with a <c>where 1=1</c> anchor so every clause can start with <c>and</c>.
    /// </summary>
    internal static (string Sql, List<NpgsqlParameter> Parameters) ApplyFilters(
        string sql, VisitSessionQuery query, string? visitorHash, List<NpgsqlParameter> parameters)
    {
        if (!string.IsNullOrEmpty(visitorHash))
        {
            sql += " and visitor_hash = :visitorHash";
            parameters.Add(new NpgsqlParameter("visitorHash", visitorHash));
        }

        if (query.FromUtc.HasValue)
        {
            sql += " and started_at_utc >= :fromUtc";
            parameters.Add(new NpgsqlParameter("fromUtc", query.FromUtc.Value));
        }

        if (query.ToUtc.HasValue)
        {
            sql += " and started_at_utc <= :toUtc";
            parameters.Add(new NpgsqlParameter("toUtc", query.ToUtc.Value));
        }

        // Bots are compacted like everyone else, but they dominate the listing, so they are
        // hidden unless the admin asks for them. Matches the partial index on the table.
        if (!query.IncludeBots)
        {
            sql += " and device_class <> 'bot'";
        }

        return (sql, parameters);
    }

    private static VisitSessionEntry MapSession(DbDataReader reader) =>
        new(Id: reader.GetInt64(reader.GetOrdinal("id")),
            VisitorHash: reader.GetString(reader.GetOrdinal("visitor_hash")),
            DistinctId: reader.IsDBNull(reader.GetOrdinal("distinct_id"))
                ? null
                : reader.GetString(reader.GetOrdinal("distinct_id")),
            StartedAtUtc: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("started_at_utc")),
            EndedAtUtc: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("ended_at_utc")),
            DurationSeconds: reader.GetInt32(reader.GetOrdinal("duration_seconds")),
            PageViewCount: reader.GetInt32(reader.GetOrdinal("page_view_count")),
            EntryPath: reader.GetString(reader.GetOrdinal("entry_path")),
            ExitPath: reader.GetString(reader.GetOrdinal("exit_path")),
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
            Environment: reader.GetString(reader.GetOrdinal("environment")));
}
