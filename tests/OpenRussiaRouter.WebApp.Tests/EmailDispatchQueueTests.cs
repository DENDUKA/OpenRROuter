using FluentAssertions;
using OpenRussiaRouter.WebApp.Email;

namespace OpenRussiaRouter.WebApp.Tests;

public class EmailDispatchQueueTests
{
    private static EmailMessage Msg(string to) => new(to, "s", "b");

    [Fact]
    public async Task EnqueueAsync_makes_the_message_readable()
    {
        var queue = new EmailDispatchQueue();

        await queue.EnqueueAsync(Msg("a@x.com"));

        queue.Reader.TryRead(out var read).Should().BeTrue();
        read!.To.Should().Be("a@x.com");
    }

    [Fact]
    public async Task EnqueueBatchAsync_enqueues_all_messages_in_order()
    {
        var queue = new EmailDispatchQueue();
        var batch = new[] { Msg("1@x.com"), Msg("2@x.com"), Msg("3@x.com") };

        await queue.EnqueueBatchAsync(batch);

        var read = new List<string>();
        while (queue.Reader.TryRead(out var m))
            read.Add(m.To);

        read.Should().Equal("1@x.com", "2@x.com", "3@x.com");
    }
}
