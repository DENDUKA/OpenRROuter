using System.Data;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;

public interface IControlPlaneDbConnectionFactory
{
    IDbConnection CreateConnection();
}