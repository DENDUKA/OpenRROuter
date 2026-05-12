using System.Data;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

/// <summary>
/// Creates PostgreSQL connections for Control Plane persistence.
/// </summary>
public interface IControlPlaneDbConnectionFactory
{
    /// <summary>
    /// Creates a database connection.
    /// </summary>
    /// <returns>Database connection.</returns>
    IDbConnection CreateConnection();
}
