using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Router;

namespace OpenRussiaRouter.ApiGateway.Tests.Router;

public sealed class RouterFailoverExecutorTests
{
    [Fact]
    public async Task SendNonStreamingAsync_FirstNode5xx_RetriesSecondNode()
    {
        var controlPlane = new StubControlPlane(new Queue<GatewayRouterLease>([
            Lease("lease-1", "node-1"),
            Lease("lease-2", "node-2")
        ]));
        var proxy = new StubRouterProxy(new Queue<object>([
            new RouterProxyResponse(503, "application/json", "{}"),
            new RouterProxyResponse(200, "application/json", "{\"ok\":true}")
        ]));
        var executor = new RouterFailoverExecutor(controlPlane, proxy);

        var result = await executor.SendNonStreamingAsync("gpt-test", "{}", "req-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("node-2", result.Lease.NodeId);
        Assert.Equal(200, result.Response.StatusCode);
        Assert.Equal(["lease-1:failed"], controlPlane.Releases);
    }

    [Fact]
    public async Task SendNonStreamingAsync_FirstNode4xx_DoesNotRetry()
    {
        var controlPlane = new StubControlPlane(new Queue<GatewayRouterLease>([Lease("lease-1", "node-1")]));
        var proxy = new StubRouterProxy(new Queue<object>([new RouterProxyResponse(400, "application/json", "{}")]));
        var executor = new RouterFailoverExecutor(controlPlane, proxy);

        var result = await executor.SendNonStreamingAsync("gpt-test", "{}", "req-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("node-1", result.Lease.NodeId);
        Assert.Equal(400, result.Response.StatusCode);
        Assert.Empty(controlPlane.Releases);
        Assert.Equal(1, controlPlane.SelectCount);
    }

    [Fact]
    public async Task SendNonStreamingAsync_FirstNodeThrows_RetriesSecondNode()
    {
        var controlPlane = new StubControlPlane(new Queue<GatewayRouterLease>([
            Lease("lease-1", "node-1"),
            Lease("lease-2", "node-2")
        ]));
        var proxy = new StubRouterProxy(new Queue<object>([
            new InvalidOperationException("network"),
            new RouterProxyResponse(200, "application/json", "{}")
        ]));
        var executor = new RouterFailoverExecutor(controlPlane, proxy);

        var result = await executor.SendNonStreamingAsync("gpt-test", "{}", "req-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("node-2", result.Lease.NodeId);
        Assert.Equal(["lease-1:failed"], controlPlane.Releases);
    }

    private static GatewayRouterLease Lease(string leaseId, string nodeId)
    {
        return new GatewayRouterLease(leaseId, nodeId, new Uri($"https://{nodeId}.example"), "token");
    }

    private sealed class StubControlPlane(Queue<GatewayRouterLease> leases) : IControlPlaneGatewayClient
    {
        public List<string> Releases { get; } = [];

        public int SelectCount { get; private set; }

        public Task<GatewayRouterLease?> SelectRouterNodeAsync(string modelId, string requestId, CancellationToken cancellationToken)
        {
            SelectCount++;
            return Task.FromResult(leases.Count == 0 ? null : leases.Dequeue());
        }

        public Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken cancellationToken)
        {
            Releases.Add($"{leaseId}:{status}");
            return Task.CompletedTask;
        }
    }

    private sealed class StubRouterProxy(Queue<object> outcomes) : IRouterProxyClient
    {
        public Task<RouterProxyResponse> SendChatCompletionAsync(
            GatewayRouterLease lease,
            string requestJson,
            string requestId,
            CancellationToken cancellationToken)
        {
            var outcome = outcomes.Dequeue();
            if (outcome is Exception exception)
            {
                return Task.FromException<RouterProxyResponse>(exception);
            }

            return Task.FromResult((RouterProxyResponse)outcome);
        }
    }
}
