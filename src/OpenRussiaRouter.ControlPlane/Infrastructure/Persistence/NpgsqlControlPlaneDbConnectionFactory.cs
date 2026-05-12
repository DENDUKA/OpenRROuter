using System.Data;
using Npgsql;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

public sealed class NpgsqlControlPlaneDbConnectionFactory : IControlPlaneDbConnectionFactory, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlControlPlaneDbConnectionFactory(string connectionString)
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
