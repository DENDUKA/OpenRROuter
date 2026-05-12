using OpenRussiaRouter.ApiGateway.ControlPlane;

namespace OpenRussiaRouter.ApiGateway.Router;

/// <summary>
/// Proxies public Gateway requests to selected 9Router nodes.
/// </summary>
public interface IRouterProxyClient
{
    /// <summary>
    /// Sends a non-streaming chat completion request to 9Router.
    /// </summary>
    /// <param name="lease">Selected router node lease.</param>
    /// <param name="requestJson">OpenAI-compatible request JSON.</param>
    /// <param name="requestId">Gateway request id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Router response.</returns>
    Task<RouterProxyResponse> SendChatCompletionAsync(
        GatewayRouterLease lease,
        string requestJson,
        string requestId,
        CancellationToken cancellationToken);
}

