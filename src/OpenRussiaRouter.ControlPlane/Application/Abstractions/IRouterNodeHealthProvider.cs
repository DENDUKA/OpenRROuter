using OpenRussiaRouter.ControlPlane.Application.Contracts;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Abstractions;

public interface IRouterNodeHealthProvider
{

    Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken);
}
