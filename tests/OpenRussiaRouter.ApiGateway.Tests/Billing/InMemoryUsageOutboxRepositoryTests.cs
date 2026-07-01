using FluentAssertions;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Tests.Billing;

public sealed class InMemoryUsageOutboxRepositoryTests
{
    private static readonly GatewayUsageEvent SampleEvent = CreateEvent("req-1");

    [Fact]
    public async Task EnqueueAsync_Idempotent_DoesNotDuplicate()
    {
        var repo = new InMemoryUsageOutboxRepository();

        await repo.EnqueueAsync(SampleEvent, CancellationToken.None);
        await repo.EnqueueAsync(SampleEvent, CancellationToken.None);

        repo.GetAll().Should().ContainSingle();
    }

    [Fact]
    public async Task ClaimPendingAsync_NotReady_ReturnsEmpty()
    {
        var repo = new InMemoryUsageOutboxRepository();
        await repo.EnqueueAsync(SampleEvent, CancellationToken.None);

        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var claimed = await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);

        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimPendingAsync_LeasedNotExpired_ReturnsEmpty()
    {
        var repo = new InMemoryUsageOutboxRepository();
        await repo.EnqueueAsync(SampleEvent, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var claimed = await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);
        var secondClaim = await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);

        claimed.Should().ContainSingle();
        secondClaim.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimPendingAsync_LeasedExpired_ReturnsRecord()
    {
        var repo = new InMemoryUsageOutboxRepository();
        await repo.EnqueueAsync(SampleEvent, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);

        var afterLease = now.Add(TimeSpan.FromMinutes(2));
        var claimed = await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), afterLease, CancellationToken.None);

        claimed.Should().ContainSingle();
    }

    [Fact]
    public async Task MarkSentAsync_DeletesRecord()
    {
        var repo = new InMemoryUsageOutboxRepository();
        await repo.EnqueueAsync(SampleEvent, CancellationToken.None);

        await repo.MarkSentAsync(SampleEvent.RequestId, CancellationToken.None);

        repo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task MarkFailedAsync_UpdatesAttemptsAndNextAttemptAndClearsLease()
    {
        var repo = new InMemoryUsageOutboxRepository();
        await repo.EnqueueAsync(SampleEvent, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var claimed = await repo.ClaimPendingAsync(10, TimeSpan.FromMinutes(1), now, CancellationToken.None);
        claimed.Should().ContainSingle();

        var nextAttempt = now.Add(TimeSpan.FromMinutes(5));
        await repo.MarkFailedAsync(SampleEvent.RequestId, attempts: 3, nextAttempt, CancellationToken.None);

        var record = repo.GetAll().Single();
        record.Attempts.Should().Be(3);
        record.NextAttemptAtUtc.Should().Be(nextAttempt);
        record.LeasedUntilUtc.Should().BeNull();
    }

    private static GatewayUsageEvent CreateEvent(string requestId)
    {
        var now = DateTimeOffset.UtcNow;
        return new GatewayUsageEvent(
            requestId,
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
            Endpoint: "chat_completions",
            Stream: false);
    }
}
