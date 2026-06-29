using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.ControlPlane.Application.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;
using OpenRussiaRouter.ControlPlane.Infrastructure.Workers;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterLeaseCleanupWorkerTests
{
    [Fact]
    public async Task RunOnce_ExpiredLease_ReleasesLoad()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
        var now = DateTimeOffset.UtcNow;
        var node = await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        await controlPlane.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, now);
        await controlPlane.SelectRouterNodeAsync(null, "req-1", now);
        var worker = new RouterLeaseCleanupWorker(
            controlPlane,
            TestOptionsMonitor.Create(new RouterLeaseCleanupOptions()),
            NullLogger<RouterLeaseCleanupWorker>.Instance);

        await worker.RunOnceAsync(now.AddMinutes(6));

        var summary = (await controlPlane.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(0);
    }

    [Fact]
    public async Task RunOnce_Disabled_DoesNotReleaseExpiredLease()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
        var now = DateTimeOffset.UtcNow;
        var node = await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, now);
        await controlPlane.ApplyHealthAsync(node.Id, RouterNodeStatus.Healthy, now);
        await controlPlane.SelectRouterNodeAsync(null, "req-1", now);
        var worker = new RouterLeaseCleanupWorker(
            controlPlane,
            TestOptionsMonitor.Create(new RouterLeaseCleanupOptions { Enabled = false }),
            NullLogger<RouterLeaseCleanupWorker>.Instance);

        await worker.RunOnceAsync(now.AddMinutes(6));

        var summary = (await controlPlane.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.InFlightRequests.Should().Be(1);
    }
}
