using Npgsql;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>PostgreSQL-backed Yandex Metrica identity store.</summary>
public sealed class PostgresYandexMetrikaIdentityStore(NpgsqlDataSource dataSource)
    : IYandexMetrikaIdentityStore
{
    /// <inheritdoc />
    public async Task UpsertAsync(
        string userId,
        string clientId,
        DateTimeOffset observedAtUtc,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        const string sql = """
            insert into analytics.yandex_metrika_identities
                (user_id, client_id, first_seen_at_utc, last_seen_at_utc)
            values (@userId, @clientId, @observedAtUtc, @observedAtUtc)
            on conflict (user_id) do update set
                client_id = case
                    when excluded.last_seen_at_utc >= analytics.yandex_metrika_identities.last_seen_at_utc
                    then excluded.client_id
                    else analytics.yandex_metrika_identities.client_id
                end,
                first_seen_at_utc = least(
                    analytics.yandex_metrika_identities.first_seen_at_utc,
                    excluded.first_seen_at_utc),
                last_seen_at_utc = greatest(
                    analytics.yandex_metrika_identities.last_seen_at_utc,
                    excluded.last_seen_at_utc);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("clientId", clientId);
        command.Parameters.AddWithValue("observedAtUtc", observedAtUtc);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> GetClientIdAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        const string sql = """
            select client_id
            from analytics.yandex_metrika_identities
            where user_id = @userId;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        return (string?)await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindRelatedUserIdsAsync(
        string clientId,
        string? excludeUserId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        const string sql = """
            select user_id
            from analytics.yandex_metrika_identities
            where client_id = @clientId
              and (@excludeUserId is null or user_id <> @excludeUserId)
            order by last_seen_at_utc desc;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("clientId", clientId);
        command.Parameters.AddWithValue("excludeUserId", (object?)excludeUserId ?? DBNull.Value);

        var userIds = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            userIds.Add(reader.GetString(0));
        }

        return userIds;
    }
}
