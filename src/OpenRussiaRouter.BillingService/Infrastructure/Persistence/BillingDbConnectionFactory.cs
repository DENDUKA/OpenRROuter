using System.Data;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class BillingDbConnectionFactory : IBillingDbConnectionFactory, IAsyncDisposable
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="BillingDbConnectionFactory"/> class.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    public BillingDbConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        _dataSource = dataSourceBuilder.Build();
    }

    /// <inheritdoc />
    public IDbConnection CreateConnection() => _dataSource.CreateConnection();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}