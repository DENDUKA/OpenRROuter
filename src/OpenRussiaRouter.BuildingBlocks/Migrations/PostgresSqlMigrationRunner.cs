using Npgsql;

namespace OpenRussiaRouter.BuildingBlocks.Migrations;

public sealed class PostgresSqlMigrationRunner(string connectionString)
{
    public async Task ApplyAsync(string service, IReadOnlyList<SqlMigration> migrations, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentNullException.ThrowIfNull(migrations);

        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await EnsureMigrationTableAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var migration in migrations)
        {
            await ApplyMigrationAsync(connection, service, migration, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = connectionStringBuilder.Database;
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        connectionStringBuilder.Database = "postgres";
        connectionStringBuilder.Pooling = false;

        await using var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string existsSql = "select exists (select 1 from pg_database where datname = @databaseName);";
        await using (var existsCommand = new NpgsqlCommand(existsSql, connection))
        {
            existsCommand.Parameters.AddWithValue("databaseName", databaseName);
            var exists = (bool)(await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            if (exists)
            {
                return;
            }
        }

        await using var createCommand = new NpgsqlCommand($"create database {QuoteIdentifier(databaseName)}", connection);
        await createCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static async Task EnsureMigrationTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            create table if not exists public.schema_migrations (
                service varchar(64) not null,
                version varchar(128) not null,
                applied_at_utc timestamptz not null,
                primary key (service, version)
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyMigrationAsync(
        NpgsqlConnection connection,
        string service,
        SqlMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var alreadyApplied = await IsMigrationAppliedAsync(connection, transaction, service, migration.Version, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyApplied)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using (var migrationCommand = new NpgsqlCommand(migration.Sql, connection, transaction))
        {
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        const string insertSql = """
            insert into public.schema_migrations (service, version, applied_at_utc)
            values (@service, @version, now());
            """;

        await using (var insertCommand = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insertCommand.Parameters.AddWithValue("service", service);
            insertCommand.Parameters.AddWithValue("version", migration.Version);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsMigrationAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string service,
        string version,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from public.schema_migrations
                where service = @service and version = @version
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("service", service);
        command.Parameters.AddWithValue("version", version);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }
}
