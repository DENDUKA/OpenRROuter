using Npgsql;

namespace OpenRussiaRouter.ApiGateway.Billing;

public interface IGatewayDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}

public sealed class GatewayDbConnectionFactory(string connectionString) : IGatewayDbConnectionFactory
{
    public NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(connectionString);
    }
}
