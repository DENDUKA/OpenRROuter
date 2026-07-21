using System.Data;

namespace OpenRussiaRouter.ContentService.Infrastructure.Persistence;

public sealed class ContentDbConnectionFactory : IContentDbConnectionFactory, IAsyncDisposable
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;

    public ContentDbConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        _dataSource = dataSourceBuilder.Build();
    }

    public IDbConnection CreateConnection() => _dataSource.CreateConnection();

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
