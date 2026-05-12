using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application;
using OpenRussiaRouter.ControlPlane.Domain;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterHealthCheckWorkerTests
{
    [Fact]
    public async Task RunOnceAsync_HealthyResult_UpdatesNodeStatus()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        controlPlane.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var worker = new RouterHealthCheckWorker(
            controlPlane,
            new StubHealthClient(new RouterNodeHealthResult(RouterNodeStatus.Healthy, 17, null)),
            TestOptionsMonitor.Create(new RouterHealthCheckOptions()),
            NullLogger<RouterHealthCheckWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        var summary = Assert.Single(controlPlane.ListRouterNodes());
        Assert.Equal(RouterNodeStatus.Healthy, summary.Status);
        Assert.NotNull(summary.LastCheckedAtUtc);
    }

    [Fact]
    public async Task RunOnceAsync_DisabledNode_DoesNotCallHealthClient()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var node = controlPlane.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        controlPlane.DisableNode(node.Id);
        var healthClient = new CountingHealthClient();
        var worker = new RouterHealthCheckWorker(
            controlPlane,
            healthClient,
            TestOptionsMonitor.Create(new RouterHealthCheckOptions()),
            NullLogger<RouterHealthCheckWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, healthClient.CallCount);
        var summary = Assert.Single(controlPlane.ListRouterNodes());
        Assert.Equal(RouterNodeStatus.Disabled, summary.Status);
    }

    private sealed class StubHealthClient(RouterNodeHealthResult result) : IRouterNodeHealthClient
    {
        public Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class CountingHealthClient : IRouterNodeHealthClient
    {
        public int CallCount { get; private set; }

        public Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new RouterNodeHealthResult(RouterNodeStatus.Healthy, 1, null));
        }
    }
}
