using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenRussiaRouter.WebApp.Email;

namespace OpenRussiaRouter.WebApp.Tests;

public class EmailDispatchWorkerTests
{
    /// <summary>
    /// A persistently-failing message is retried up to 3 times then discarded, and the
    /// worker carries on to the next message (proving one bad address can't wedge the queue).
    /// Uses <see cref="FakeTimeProvider"/> so the 1s/2s retry backoff is advanced without
    /// real-time waits — keeps the test deterministic and fast under CI load.
    /// </summary>
    [Fact]
    public async Task Persistent_failure_retries_three_times_then_discards_and_continues()
    {
        var sender = new RecordingSender(failFor: "fail@x.com");
        var scopeFactory = new ServiceCollection()
            .AddSingleton<IEmailSender>(sender)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var queue = new EmailDispatchQueue();
        var timeProvider = new FakeTimeProvider();
        // BatchDelayMs = 0 so only the retry backoff matters, not throttling.
        var worker = new EmailDispatchWorker(
            queue, scopeFactory, NullLogger<EmailDispatchWorker>.Instance,
            Options.Create(new EmailSettings { BatchDelayMs = 0 }),
            timeProvider);

        await queue.EnqueueAsync(new EmailMessage("fail@x.com", "s", "b"));
        await queue.EnqueueAsync(new EmailMessage("ok@x.com", "s", "b"));

        await worker.StartAsync(CancellationToken.None);

        // Drive the worker through both retry backoffs (1s + 2s) plus a generous safety margin.
        // Each Advance kicks pending Task.Delay(FakeTimeProvider) continuations; the worker
        // then observes them on its next await point.
        for (int i = 0; i < 4; i++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            // Yield to let the worker's background loop react to the time advance before
            // progressing further.
            await Task.Yield();
        }

        // The "ok" message is only reached after the failing one is exhausted+discarded.
        // Use a real-time timeout as a last-resort safety net only — the FakeTimeProvider
        // advances above should already have let the worker reach it.
        var completed = await Task.WhenAny(sender.OkDelivered.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await worker.StopAsync(CancellationToken.None);

        completed.Should().Be(sender.OkDelivered.Task, "the ok message should be delivered after backoff");
        sender.AttemptsFor("fail@x.com").Should().Be(3);
        sender.AttemptsFor("ok@x.com").Should().Be(1);
    }

    private sealed class RecordingSender : IEmailSender
    {
        private readonly string _failFor;
        private readonly Dictionary<string, int> _attempts = new();
        private readonly object _lock = new();

        public TaskCompletionSource OkDelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingSender(string failFor) => _failFor = failFor;

        public int AttemptsFor(string to)
        {
            lock (_lock) return _attempts.GetValueOrDefault(to, 0);
        }

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            lock (_lock) _attempts[message.To] = _attempts.GetValueOrDefault(message.To, 0) + 1;

            if (message.To == _failFor)
                throw new InvalidOperationException("simulated SMTP failure");

            OkDelivered.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
