using System.Data;
using Npgsql;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

/// <summary>
/// Npgsql connection factory for Control Plane.
/// </summary>
public sealed class NpgsqlControlPlaneDbConnectionFactory(string connectionString) : IControlPlaneDbConnectionFactory
{
    /// <inheritdoc />
    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(connectionString);
    }
}
