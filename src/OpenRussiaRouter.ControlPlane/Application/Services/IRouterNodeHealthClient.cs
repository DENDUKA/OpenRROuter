using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Checks external 9Router node health.
/// </summary>
public interface IRouterNodeHealthClient
{
    /// <summary>
    /// Performs a health check for one node.
    /// </summary>
    /// <param name="node">Router node.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health result.</returns>
    Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken);
}
