using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Application;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterLeaseCleanupWorkerTests
{
    [Fact]
    public void RunOnce_ExpiredLease_ReleasesLoad()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var now = DateTimeOffset.UtcNow;
        var node = controlPlane.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        controlPlane.ApplyHealth(node.Id, RouterNodeStatus.Healthy, now);
        controlPlane.SelectRouterNode("req-1", now);
        var worker = new RouterLeaseCleanupWorker(
            controlPlane,
            TestOptionsMonitor.Create(new RouterLeaseCleanupOptions()),
            NullLogger<RouterLeaseCleanupWorker>.Instance);

        worker.RunOnce(now.AddMinutes(6));

        var summary = Assert.Single(controlPlane.ListRouterNodes());
        Assert.Equal(0, summary.InFlightRequests);
    }

    [Fact]
    public void RunOnce_Disabled_DoesNotReleaseExpiredLease()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository());
        var now = DateTimeOffset.UtcNow;
        var node = controlPlane.RegisterNode("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        controlPlane.ApplyHealth(node.Id, RouterNodeStatus.Healthy, now);
        controlPlane.SelectRouterNode("req-1", now);
        var worker = new RouterLeaseCleanupWorker(
            controlPlane,
            TestOptionsMonitor.Create(new RouterLeaseCleanupOptions { Enabled = false }),
            NullLogger<RouterLeaseCleanupWorker>.Instance);

        worker.RunOnce(now.AddMinutes(6));

        var summary = Assert.Single(controlPlane.ListRouterNodes());
        Assert.Equal(1, summary.InFlightRequests);
    }
}
