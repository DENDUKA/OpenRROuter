using System.Data;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

/// <summary>
/// Creates PostgreSQL connections for Auth Service persistence.
/// </summary>
public interface IAuthDbConnectionFactory
{
    /// <summary>
    /// Creates a database connection.
    /// </summary>
    /// <returns>Database connection.</returns>
    IDbConnection CreateConnection();
}
