using System.Data;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

/// <summary>
/// Creates PostgreSQL connections for Billing Service persistence.
/// </summary>
public interface IBillingDbConnectionFactory
{
    /// <summary>
    /// Creates a database connection.
    /// </summary>
    /// <returns>Database connection.</returns>
    IDbConnection CreateConnection();
}
