using System.Data;
using Npgsql;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class NpgsqlBillingDbConnectionFactory : IBillingDbConnectionFactory, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlBillingDbConnectionFactory(string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        _dataSource = dataSourceBuilder.Build();
    }

    public IDbConnection CreateConnection()
    {
        return _dataSource.CreateConnection();
    }

    public ValueTask DisposeAsync()
    {
        return _dataSource.DisposeAsync();
    }
}
