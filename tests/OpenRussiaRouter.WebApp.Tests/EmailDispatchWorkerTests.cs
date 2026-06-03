using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Email;

namespace OpenRussiaRouter.WebApp.Tests;

public class EmailDispatchWorkerTests
{
    /// <summary>
    /// A persistently-failing message is retried up to 3 times then discarded, and the
    /// worker carries on to the next message (proving one bad address can't wedge the queue).
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
        // BatchDelayMs = 0 so only the retry backoff (1s + 2s) costs time, not throttling.
        var worker = new EmailDispatchWorker(
            queue, scopeFactory, NullLogger<EmailDispatchWorker>.Instance,
            Options.Create(new EmailSettings { BatchDelayMs = 0 }));

        await queue.EnqueueAsync(new EmailMessage("fail@x.com", "s", "b"));
        await queue.EnqueueAsync(new EmailMessage("ok@x.com", "s", "b"));

        await worker.StartAsync(CancellationToken.None);
        // The "ok" message is only reached after the failing one is exhausted+discarded.
        var completed = await Task.WhenAny(sender.OkDelivered.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        await worker.StopAsync(CancellationToken.None);

        completed.Should().Be(sender.OkDelivered.Task, "the ok message should be delivered within the timeout");
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
