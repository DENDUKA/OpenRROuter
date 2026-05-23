using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterLeaseCleanupWorkerTests
{
    [Fact]
    public async Task RunOnce_ExpiredLease_ReleasesLoad()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var now = DateTimeOffset.UtcNow;
        var node = await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        await controlPlane.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, now);
        await controlPlane.SelectRouterNodeAsync("req-1", now);
        var worker = new RouterLeaseCleanupWorker(
            controlPlane,
            TestOptionsMonitor.Create(new RouterLeaseCleanupOptions()),
            NullLogger<RouterLeaseCleanupWorker>.Instance);

        await worker.RunOnceAsync(now.AddMinutes(6));

        var summary = (await controlPlane.ListRouterNodesAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(0);
    }

    [Fact]
    public async Task RunOnce_Disabled_DoesNotReleaseExpiredLease()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var now = DateTimeOffset.UtcNow;
        var node = await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        await controlPlane.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, now);
        await controlPlane.SelectRouterNodeAsync("req-1", now);
        var worker = new RouterLeaseCleanupWorker(
            controlPlane,
            TestOptionsMonitor.Create(new RouterLeaseCleanupOptions { Enabled = false }),
            NullLogger<RouterLeaseCleanupWorker>.Instance);

        await worker.RunOnceAsync(now.AddMinutes(6));

        var summary = (await controlPlane.ListRouterNodesAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(1);
    }
}
