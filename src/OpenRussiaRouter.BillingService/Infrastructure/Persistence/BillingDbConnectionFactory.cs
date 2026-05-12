using System.Data;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class BillingDbConnectionFactory : IBillingDbConnectionFactory, IAsyncDisposable
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;

    public BillingDbConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        _dataSource = dataSourceBuilder.Build();
    }

    public IDbConnection CreateConnection() => _dataSource.CreateConnection();

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}