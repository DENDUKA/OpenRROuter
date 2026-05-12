using System.Data;
using Npgsql;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

/// <summary>
/// Npgsql connection factory for Auth Service.
/// </summary>
public sealed class NpgsqlAuthDbConnectionFactory(string connectionString) : IAuthDbConnectionFactory
{
    /// <inheritdoc />
    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(connectionString);
    }
}
