using System.Data;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public interface IAuthDbConnectionFactory
{
    IDbConnection CreateConnection();
}