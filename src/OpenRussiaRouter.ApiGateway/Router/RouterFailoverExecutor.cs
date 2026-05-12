using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.ControlPlane.Interfaces;
using OpenRussiaRouter.ApiGateway.Router.Interfaces;

namespace OpenRussiaRouter.ApiGateway.Router;

/// <summary>
/// Executes non-streaming router requests with one safe failover before a response is returned to the client.
/// </summary>
public sealed class RouterFailoverExecutor(
    IControlPlaneGatewayClient controlPlane,
    IRouterProxyClient routerProxy)
{
    /// <summary>
    /// Sends a non-streaming request and retries once on another node for network errors or 5xx responses.
    /// </summary>
    /// <param name="modelId">Public model id.</param>
    /// <param name="requestJson">OpenAI-compatible request JSON.</param>
    /// <param name="requestId">Gateway request id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lease and router response, or null when no router is available.</returns>
    public async Task<RouterFailoverResult?> SendNonStreamingAsync(
        string modelId,
        string requestJson,
        string requestId,
        CancellationToken cancellationToken)
    {
        GatewayRouterLease? lastLease = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var lease = await controlPlane.SelectRouterNodeAsync(modelId, requestId, cancellationToken)
                .ConfigureAwait(false);
            if (lease is null)
            {
                return null;
            }

            lastLease = lease;
            RouterProxyResponse response;
            try
            {
                response = await routerProxy.SendChatCompletionAsync(lease, requestJson, requestId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await controlPlane.ReleaseRouterNodeAsync(lease.LeaseId, "failed", CancellationToken.None)
                    .ConfigureAwait(false);
                if (attempt == 0)
                {
                    continue;
                }

                throw;
            }

            if (attempt == 0 && response.StatusCode >= 500)
            {
                await controlPlane.ReleaseRouterNodeAsync(lease.LeaseId, "failed", CancellationToken.None)
                    .ConfigureAwait(false);
                continue;
            }

            return new RouterFailoverResult(lease, response);
        }

        return lastLease is null ? null : throw new InvalidOperationException("Router failover did not produce a response.");
    }
}

/// <summary>
/// Router failover execution result.
/// </summary>
/// <param name="Lease">Lease used by the final attempt.</param>
/// <param name="Response">Final router response.</param>
public sealed record RouterFailoverResult(GatewayRouterLease Lease, RouterProxyResponse Response);
