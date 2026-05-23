using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterHealthCheckWorkerTests
{
    [Fact]
    public async Task RunOnceAsync_HealthyResult_UpdatesNodeStatus()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var worker = new RouterHealthCheckWorker(
            controlPlane,
            new StubHealthClient(new RouterNodeHealthResult(RouterNodeStatus.Healthy, 17, null)),
            TestOptionsMonitor.Create(new RouterHealthCheckOptions()),
            NullLogger<RouterHealthCheckWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        var summary = (await controlPlane.ListRouterNodesAsync()).Should().ContainSingle().Which;
        summary.Status.Should().Be(RouterNodeStatus.Healthy);
        summary.LastCheckedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnceAsync_DisabledNode_DoesNotCallHealthClient()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var node = await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        await controlPlane.DisableNodeAsync(node.Id);
        var healthClient = new CountingHealthClient();
        var worker = new RouterHealthCheckWorker(
            controlPlane,
            healthClient,
            TestOptionsMonitor.Create(new RouterHealthCheckOptions()),
            NullLogger<RouterHealthCheckWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        healthClient.CallCount.Should().Be(0);
        var summary = (await controlPlane.ListRouterNodesAsync()).Should().ContainSingle().Which;
        summary.Status.Should().Be(RouterNodeStatus.Disabled);
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
