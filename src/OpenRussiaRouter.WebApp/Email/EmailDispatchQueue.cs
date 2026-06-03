using System.Threading.Channels;

namespace OpenRussiaRouter.WebApp.Email;

/// <summary>
/// In-memory bounded queue for outbound email. Bounded so a runaway broadcast
/// can't exhaust memory; writers block (Wait) when full.
/// NOTE: in-memory => messages still queued at shutdown are lost. For
/// at-least-once delivery across restarts, back this with a DB outbox table.
/// </summary>
public class EmailDispatchQueue : IEmailDispatchQueue
{
    private readonly Channel<EmailMessage> _channel =
        Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(message, ct);

    public async ValueTask EnqueueBatchAsync(IEnumerable<EmailMessage> messages, CancellationToken ct = default)
    {
        foreach (var msg in messages)
            await _channel.Writer.WriteAsync(msg, ct);
    }

    public ChannelReader<EmailMessage> Reader => _channel.Reader;
}
