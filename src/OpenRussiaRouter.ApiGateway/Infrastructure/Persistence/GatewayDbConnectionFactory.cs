using System.Data.Common;
using Npgsql;

namespace OpenRussiaRouter.ApiGateway.Infrastructure.Persistence;

public sealed class GatewayDbConnectionFactory : IGatewayDbConnectionFactory, IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;

    public GatewayDbConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSource = dataSourceBuilder.Build();
    }

    public DbConnection CreateConnection() => dataSource.CreateConnection();

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
