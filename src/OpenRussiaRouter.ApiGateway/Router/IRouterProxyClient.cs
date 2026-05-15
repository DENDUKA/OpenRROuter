using OpenRussiaRouter.ApiGateway.ControlPlane;

namespace OpenRussiaRouter.ApiGateway.Router;

public interface IRouterProxyClient
{
    Task<RouterProxyResponse> SendChatCompletionAsync(
        GatewayRouterLease lease,
        string requestJson,
        string requestId,
        string userApiKey,
        CancellationToken cancellationToken);

    Task<RouterStreamSession> BeginStreamChatCompletionAsync(
        GatewayRouterLease lease,
        string requestJson,
        string requestId,
        string userApiKey,
        CancellationToken cancellationToken);
}

