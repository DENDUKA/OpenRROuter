using Microsoft.Extensions.Logging;
using Npgsql;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Erases all analytics events for a given user from the internal Postgres analytics
/// database. Deletion is synchronous and complete — after <see cref="PurgeUserAsync"/>
/// returns no rows with the user's <c>distinct_id</c> remain in <c>analytics.events</c>.
/// </summary>
public sealed class PostgresProductAnalyticsErasure : IProductAnalyticsErasure
{
    private readonly UserPseudonymizer _pseudonymizer;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresProductAnalyticsErasure> _logger;

    public PostgresProductAnalyticsErasure(
        UserPseudonymizer pseudonymizer,
        NpgsqlDataSource dataSource,
        ILogger<PostgresProductAnalyticsErasure> logger)
    {
        _pseudonymizer = pseudonymizer ?? throw new ArgumentNullException(nameof(pseudonymizer));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> PurgeUserAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var distinctId = _pseudonymizer.ComputeDistinctId(userId);

        const string sql = "delete from analytics.events where distinct_id = @distinctId";

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("distinctId", distinctId);

        var deleted = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Erased {DeletedCount} analytics events for pseudonymized user",
            deleted);

        return deleted;
    }
}
