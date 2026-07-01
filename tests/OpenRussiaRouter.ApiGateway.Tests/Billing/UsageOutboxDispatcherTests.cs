using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Tests.Billing;

public sealed class UsageOutboxDispatcherTests
{
    private const int MaxAttempts = 3;

    [Fact]
    public async Task DispatchAsync_Success_DeletesRecordAndForwardsEndpointAndStream()
    {
        var repo = new InMemoryUsageOutboxRepository();
        var provider = new StubBillingProvider { ReturnValue = true };
        var dispatcher = CreateDispatcher(repo, provider);

        var ev = CreateEvent(endpoint: "chat_completions", stream: false);
        await repo.EnqueueAsync(ev, CancellationToken.None);

        await dispatcher.DispatchAsync(CancellationToken.None);

        repo.GetAll().Should().BeEmpty();
        provider.LastEvent.Should().NotBeNull();
        provider.LastEvent!.Endpoint.Should().Be("chat_completions");
        provider.LastEvent.Stream.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_RpcException_RetriesWithBackoff()
    {
        var repo = new InMemoryUsageOutboxRepository();
        var provider = new StubBillingProvider { Exception = new RpcException(new Status(StatusCode.Unavailable, "down")) };
        var dispatcher = CreateDispatcher(repo, provider);

        var ev = CreateEvent();
        await repo.EnqueueAsync(ev, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        await dispatcher.DispatchAsync(CancellationToken.None);

        var record = repo.GetAll().Single();
        record.Attempts.Should().Be(1);
        record.NextAttemptAtUtc.Should().BeAfter(now);
        record.LeasedUntilUtc.Should().BeNull();
    }

    [Fact]
    public async Task DispatchAsync_ExceedsMaxAttempts_KeepsRecord()
    {
        var repo = new InMemoryUsageOutboxRepository();
        var provider = new StubBillingProvider { Exception = new RpcException(new Status(StatusCode.Unavailable, "down")) };
        var dispatcher = CreateDispatcher(repo, provider, baseBackoff: TimeSpan.FromMilliseconds(1));

        var ev = CreateEvent();
        await repo.EnqueueAsync(ev, CancellationToken.None);

        for (var i = 0; i < MaxAttempts + 1; i++)
        {
            await dispatcher.DispatchAsync(CancellationToken.None);
            if (i < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
        }

        var record = repo.GetAll().Single();
        record.Attempts.Should().BeGreaterOrEqualTo(MaxAttempts);
    }

    [Fact]
    public async Task DispatchAsync_StreamEvent_ForwardsStreamTrue()
    {
        var repo = new InMemoryUsageOutboxRepository();
        var provider = new StubBillingProvider { ReturnValue = true };
        var dispatcher = CreateDispatcher(repo, provider);

        var ev = CreateEvent(endpoint: "chat_completions", stream: true);
        await repo.EnqueueAsync(ev, CancellationToken.None);

        await dispatcher.DispatchAsync(CancellationToken.None);

        provider.LastEvent.Should().NotBeNull();
        provider.LastEvent!.Stream.Should().BeTrue();
    }

    private static UsageOutboxDispatcher CreateDispatcher(
        IUsageOutboxRepository repository,
        IBillingProvider billingProvider,
        TimeSpan? baseBackoff = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(billingProvider);
        var provider = services.BuildServiceProvider();

        var options = Options.Create(new UsageOutboxOptions
        {
            BatchSize = 50,
            PollInterval = TimeSpan.FromSeconds(2),
            MaxAttempts = MaxAttempts,
            BaseBackoff = baseBackoff ?? TimeSpan.FromSeconds(5),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        return new UsageOutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<UsageOutboxDispatcher>.Instance);
    }

    private static GatewayUsageEvent CreateEvent(string? endpoint = null, bool? stream = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new GatewayUsageEvent(
            "req-1",
            "user-1",
            "key-1",
            "sk-or-test",
            "gpt-4",
            "node-1",
            new TokenUsage(10, 5, 0),
            "completed",
            now.AddSeconds(-1),
            now,
            ProviderName: "prov",
            ProviderAccount: "acct",
            Endpoint: endpoint,
            Stream: stream);
    }

    private sealed class StubBillingProvider : IBillingProvider
    {
        public bool ReturnValue { get; set; } = true;
        public RpcException? Exception { get; set; }
        public GatewayUsageEvent? LastEvent { get; private set; }

        public Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<GatewayPublicModel>>(Array.Empty<GatewayPublicModel>());

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(
            string userId,
            string apiKeyId,
            string modelId,
            bool adminBypassDisabled,
            CancellationToken ct)
            => Task.FromResult(new GatewayBalanceCheckResult(true, modelId, modelId, null, BuildingBlocks.Money.MoneyAmount.Usd(0), null));

        public Task<bool> RecordUsageAsync(GatewayUsageEvent ev, CancellationToken ct)
        {
            LastEvent = ev;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(ReturnValue);
        }
    }
}
