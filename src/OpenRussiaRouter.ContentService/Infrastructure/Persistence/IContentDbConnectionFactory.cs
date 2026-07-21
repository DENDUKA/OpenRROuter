using System.Data;

namespace OpenRussiaRouter.ContentService.Infrastructure.Persistence;

public interface IContentDbConnectionFactory
{
    IDbConnection CreateConnection();
}
