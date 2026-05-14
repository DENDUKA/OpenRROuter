using System.Data.Common;

namespace OpenRussiaRouter.ApiGateway.Infrastructure.Persistence;

public interface IGatewayDbConnectionFactory
{
    DbConnection CreateConnection();
}
