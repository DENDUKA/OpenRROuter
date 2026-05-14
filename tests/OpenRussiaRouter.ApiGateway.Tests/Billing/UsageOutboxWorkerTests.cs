using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.ApiGateway.Application.Extensions;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Tests.Billing;

public sealed class UsageOutboxWorkerTests
{
    [Fact]
    public async Task RunOnceAsync_AcceptedDelivery_MarksEventDelivered()
    {
        var repository = CreateRepository();
        var billingClient = new StubBillingClient { Accepted = true };
        var worker = CreateWorker(repository, billingClient);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);

        await repository.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);
        await worker.RunOnceAsync(now, CancellationToken.None);

        var leasedAgain = await repository.LeaseDueAsync(10, "test", TimeSpan.FromMinutes(1), now.AddHours(1), CancellationToken.None);
        Assert.Empty(leasedAgain);
        Assert.Equal(1, billingClient.RecordUsageCalls);
    }

    [Fact]
    public async Task RunOnceAsync_FailedDelivery_SchedulesRetry()
    {
        var repository = CreateRepository();
        var billingClient = new StubBillingClient { Accepted = false };
        var worker = CreateWorker(repository, billingClient);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);

        await repository.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);
        await worker.RunOnceAsync(now, CancellationToken.None);

        var notYetDue = await repository.LeaseDueAsync(10, "test", TimeSpan.FromMinutes(1), now, CancellationToken.None);
        Assert.Empty(notYetDue);
        Assert.Equal(1, billingClient.RecordUsageCalls);
    }

    [Fact]
    public async Task RunOnceAsync_MaxAttempts_MovesEventToDead()
    {
        var repository = CreateRepository();
        var billingClient = new StubBillingClient { ThrowOnRecordUsage = true };
        var worker = CreateWorker(repository, billingClient, maxAttempts: 1);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);

        await repository.EnqueueAsync(CreateEvent("req-1"), CancellationToken.None);
        await worker.RunOnceAsync(now, CancellationToken.None);

        var leasedAgain = await repository.LeaseDueAsync(10, "test", TimeSpan.FromMinutes(1), now.AddHours(1), CancellationToken.None);
        Assert.Empty(leasedAgain);
        Assert.Equal(1, billingClient.RecordUsageCalls);
    }

    private static UsageOutboxWorker CreateWorker(
        IUsageOutboxRepository repository,
        IBillingGatewayClient billingClient,
        int maxAttempts = 20)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => billingClient)
            .BuildServiceProvider();

        return new UsageOutboxWorker(
            repository,
            services.GetRequiredService<IServiceScopeFactory>(),
            TestOptionsMonitor.Create(new UsageOutboxOptions
            {
                Enabled = true,
                BatchSize = 10,
                BaseBackoffSeconds = 2,
                MaxBackoffSeconds = 300,
                MaxAttempts = maxAttempts
            }),
            NullLogger<UsageOutboxWorker>.Instance);
    }

    private static InMemoryUsageOutboxRepository CreateRepository()
    {
        return new InMemoryUsageOutboxRepository(NullLogger<InMemoryUsageOutboxRepository>.Instance);
    }

    private static GatewayUsageEvent CreateEvent(string requestId)
    {
        return new GatewayUsageEvent(
            requestId,
            "user-1",
            "key-1",
            "gpt-test",
            "node-1",
            new TokenUsage(10, 20, 0),
            "completed",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow);
    }

    private sealed class StubBillingClient : IBillingGatewayClient
    {
        public bool Accepted { get; init; } = true;

        public bool ThrowOnRecordUsage { get; init; }

        public int RecordUsageCalls { get; private set; }

        public Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<GatewayPublicModel>>([]);
        }

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(string userId, string modelId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GatewayBalanceCheckResult(true, modelId, "v1", MoneyAmount.Rub(100), null));
        }

        public Task<bool> RecordUsageAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken)
        {
            RecordUsageCalls++;
            if (ThrowOnRecordUsage)
            {
                throw new InvalidOperationException("billing unavailable");
            }

            return Task.FromResult(Accepted);
        }
    }
}
