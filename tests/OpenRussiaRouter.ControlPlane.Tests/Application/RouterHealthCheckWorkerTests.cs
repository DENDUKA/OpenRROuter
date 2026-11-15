using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;
using OpenRussiaRouter.ControlPlane.Application.Contracts;
using OpenRussiaRouter.ControlPlane.Application.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Infrastructure.Http;
using OpenRussiaRouter.ControlPlane.Infrastructure.Repository;
using OpenRussiaRouter.ControlPlane.Infrastructure.Workers;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterHealthCheckWorkerTests
{
    [Fact]
    public async Task RunOnceAsync_HealthyResult_UpdatesNodeStatus()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
        await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var worker = new RouterHealthCheckWorker(
            controlPlane,
            new StubHealthClient(new RouterNodeHealthResult(RouterNodeStatus.Healthy, 17, null)),
            TestOptionsMonitor.Create(new RouterHealthCheckOptions()),
            NullLogger<RouterHealthCheckWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        var summary = (await controlPlane.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.Status.Should().Be(RouterNodeStatus.Healthy);
        summary.LastCheckedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnceAsync_DisabledNode_DoesNotCallHealthClient()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
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
        var summary = (await controlPlane.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.Status.Should().Be(RouterNodeStatus.Disabled);
    }

    [Fact]
    public async Task RunOnceAsync_SingleFailure_BelowThreshold_KeepsNodeRoutable()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
        await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var healthClient = new SequenceHealthClient(
            new RouterNodeHealthResult(RouterNodeStatus.Healthy, 10, null),
            new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "timeout"));
        var worker = new RouterHealthCheckWorker(
            controlPlane,
            healthClient,
            TestOptionsMonitor.Create(new RouterHealthCheckOptions()),
            NullLogger<RouterHealthCheckWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);

        var summary = (await controlPlane.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.Status.Should().Be(RouterNodeStatus.Healthy);
        summary.LastHealthErrorCode.Should().Be("timeout");
    }

    [Fact]
    public async Task RunOnceAsync_FailuresReachThreshold_MarksNodeUnhealthy()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
        await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var healthClient = new SequenceHealthClient(
            new RouterNodeHealthResult(RouterNodeStatus.Healthy, 10, null),
            new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "timeout"),
            new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "timeout"),
            new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "network_error"));
        var worker = new RouterHealthCheckWorker(
            controlPlane,
            healthClient,
            TestOptionsMonitor.Create(new RouterHealthCheckOptions()),
            NullLogger<RouterHealthCheckWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);
        var summary = (await controlPlane.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.Status.Should().Be(RouterNodeStatus.Healthy);

        await worker.RunOnceAsync(CancellationToken.None);

        summary = (await controlPlane.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.Status.Should().Be(RouterNodeStatus.Unhealthy);
        summary.LastHealthErrorCode.Should().Be("network_error");
    }

    [Fact]
    public async Task RunOnceAsync_SuccessResetsFailureCounter()
    {
        var controlPlane = new RouterControlPlaneService(new InMemoryControlPlaneRepository(), new NodeModelCapabilityStore());
        await controlPlane.RegisterNodeAsync("node-a", new Uri("https://router-a.example"), "secret-a", 1, DateTimeOffset.UtcNow);
        var healthClient = new SequenceHealthClient(
            new RouterNodeHealthResult(RouterNodeStatus.Healthy, 10, null),
            new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "timeout"),
            new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "timeout"),
            new RouterNodeHealthResult(RouterNodeStatus.Healthy, 12, null),
            new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "timeout"),
            new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "timeout"));
        var worker = new RouterHealthCheckWorker(
            controlPlane,
            healthClient,
            TestOptionsMonitor.Create(new RouterHealthCheckOptions()),
            NullLogger<RouterHealthCheckWorker>.Instance);

        for (var i = 0; i < 6; i++)
        {
            await worker.RunOnceAsync(CancellationToken.None);
        }

        var summary = (await controlPlane.ListNodesForHealthChecksAsync()).Should().ContainSingle().Which;
        summary.Status.Should().Be(RouterNodeStatus.Healthy);
    }

    private sealed class SequenceHealthClient(params RouterNodeHealthResult[] results) : IRouterNodeHealthProvider
    {
        private int _index;

        public Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken)
        {
            var result = results[Math.Min(_index, results.Length - 1)];
            _index++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubHealthClient(RouterNodeHealthResult result) : IRouterNodeHealthProvider
    {
        public Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class CountingHealthClient : IRouterNodeHealthProvider
    {
        public int CallCount { get; private set; }

        public Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new RouterNodeHealthResult(RouterNodeStatus.Healthy, 1, null));
        }
    }
}
