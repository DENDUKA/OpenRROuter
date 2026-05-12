using System.Data;
using Npgsql;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

/// <summary>
/// Npgsql connection factory for Billing Service.
/// </summary>
public sealed class NpgsqlBillingDbConnectionFactory(string connectionString) : IBillingDbConnectionFactory
{
    /// <inheritdoc />
    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(connectionString);
    }
}
