using System.Data;
using Npgsql;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public sealed class NpgsqlAuthDbConnectionFactory : IAuthDbConnectionFactory, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlAuthDbConnectionFactory(string connectionString)
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
