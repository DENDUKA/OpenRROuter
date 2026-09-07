using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Tests;

public class SupportBotNotifierTests
{
    private static SupportBotSettings EnabledSettings(string? endpoint = "http://bot:8080/v1/events/support",
        string? token = "secret-token") => new()
    {
        Enabled = true,
        Endpoint = endpoint,
        AuthToken = token,
    };

    private static (SupportBotEventQueue Queue, SupportBotNotifier Notifier) Create(SupportBotSettings settings)
    {
        var queue = new SupportBotEventQueue();
        var notifier = new SupportBotNotifier(queue, Options.Create(settings), NullLogger<SupportBotNotifier>.Instance);
        return (queue, notifier);
    }

    [Fact]
    public void Disabled_channel_is_a_silent_no_op()
    {
        var (queue, notifier) = Create(new SupportBotSettings { Enabled = false, Endpoint = "http://bot", AuthToken = "t" });

        notifier.NotifySupportTicket("user@example.com", "Hi", "Question.");

        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "token")]
    [InlineData("", "token")]
    [InlineData("http://bot", null)]
    [InlineData("http://bot", "")]
    public void Enabled_but_misconfigured_channel_drops_the_event_without_throwing(string? endpoint, string? token)
    {
        var (queue, notifier) = Create(EnabledSettings(endpoint, token));

        var act = () => notifier.NotifySupportTicket("user@example.com", "Hi", "Question.");

        act.Should().NotThrow("the bot channel must never break the form");
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Valid_configuration_queues_one_event_with_stable_id_and_ticket_fields()
    {
        var (queue, notifier) = Create(EnabledSettings());
        var before = DateTimeOffset.UtcNow;

        notifier.NotifySupportTicket("user@example.com", "Не проходит оплата", "Текст обращения.");

        queue.Reader.TryRead(out var evt).Should().BeTrue();
        evt!.Id.Should().MatchRegex("^[0-9a-f]{32}$", "one id per submission, reused across retries");
        evt.OccurredAt.Should().BeOnOrAfter(before - TimeSpan.FromSeconds(1));
        evt.Email.Should().Be("user@example.com");
        evt.Subject.Should().Be("Не проходит оплата");
        evt.Message.Should().Be("Текст обращения.");
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Each_submission_gets_its_own_event_id()
    {
        var (queue, notifier) = Create(EnabledSettings());

        notifier.NotifySupportTicket("user@example.com", "A", "one");
        notifier.NotifySupportTicket("user@example.com", "B", "two");

        queue.Reader.TryRead(out var first).Should().BeTrue();
        queue.Reader.TryRead(out var second).Should().BeTrue();
        first!.Id.Should().NotBe(second!.Id);
    }

    [Fact]
    public void Full_queue_drops_the_event_with_a_warning_instead_of_blocking()
    {
        var queue = new SupportBotEventQueue();
        var notifier = new SupportBotNotifier(
            queue, Options.Create(EnabledSettings()), NullLogger<SupportBotNotifier>.Instance);

        for (int i = 0; i < SupportBotEventQueue.Capacity; i++)
            queue.TryEnqueue(new SupportBotEvent($"id-{i}", DateTimeOffset.UtcNow, "a@b.c", "s", "m")).Should().BeTrue();

        var act = () => notifier.NotifySupportTicket("user@example.com", "Hi", "Question.");

        act.Should().NotThrow("a full best-effort queue drops, it never blocks the form");
        queue.Reader.TryRead(out _).Should().BeTrue(); // still drains exactly Capacity events
        int remaining = 1;
        while (queue.Reader.TryRead(out _)) remaining++;
        remaining.Should().Be(SupportBotEventQueue.Capacity, "the overflow event was dropped, not queued");
    }

    [Fact]
    public void Internal_failures_are_swallowed()
    {
        // A throwing queue must never surface in the form handler.
        var throwingQueue = new ThrowingQueue();
        var notifier = new SupportBotNotifier(
            throwingQueue, Options.Create(EnabledSettings()), NullLogger<SupportBotNotifier>.Instance);

        var act = () => notifier.NotifySupportTicket("user@example.com", "Hi", "Question.");

        act.Should().NotThrow();
        throwingQueue.Calls.Should().Be(1);
    }

    [Fact]
    public void NotifyUserRegistered_valid_configuration_queues_registration_event()
    {
        var (queue, notifier) = Create(EnabledSettings());
        var before = DateTimeOffset.UtcNow;

        notifier.NotifyUserRegistered("newuser@example.com");

        queue.Reader.TryRead(out var evt).Should().BeTrue();
        evt!.Id.Should().MatchRegex("^[0-9a-f]{32}$");
        evt.OccurredAt.Should().BeOnOrAfter(before - TimeSpan.FromSeconds(1));
        evt.Email.Should().Be("newuser@example.com");
        evt.EventType.Should().Be(SupportBotEvent.UserRegisteredType);
        evt.Subject.Should().BeNull();
        evt.Message.Should().BeNull();
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void NotifyUserRegistered_disabled_channel_is_a_silent_no_op()
    {
        var (queue, notifier) = Create(new SupportBotSettings { Enabled = false, Endpoint = "http://bot", AuthToken = "t" });

        notifier.NotifyUserRegistered("newuser@example.com");

        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    private sealed class ThrowingQueue : ISupportBotEventQueue
    {
        public int Calls { get; private set; }

        public bool TryEnqueue(SupportBotEvent supportEvent)
        {
            Calls++;
            throw new InvalidOperationException("simulated queue failure");
        }

        public System.Threading.Channels.ChannelReader<SupportBotEvent> Reader =>
            throw new NotSupportedException("not used in this test");
    }
}
