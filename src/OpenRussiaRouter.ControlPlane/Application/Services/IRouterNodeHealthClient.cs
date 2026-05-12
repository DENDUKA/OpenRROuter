using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public interface IRouterNodeHealthClient
{

    Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken);
}
