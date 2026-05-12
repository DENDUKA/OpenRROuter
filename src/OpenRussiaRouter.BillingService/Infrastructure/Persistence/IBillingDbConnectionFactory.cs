using System.Data;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public interface IBillingDbConnectionFactory
{
    IDbConnection CreateConnection();
}