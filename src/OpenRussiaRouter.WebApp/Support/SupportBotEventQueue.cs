using System.Threading.Channels;

namespace OpenRussiaRouter.WebApp.Support;

public interface ISupportBotEventQueue
{
    /// <summary>Non-blocking enqueue; false when the queue is full (caller decides to drop).</summary>
    bool TryEnqueue(SupportBotEvent supportEvent);

    ChannelReader<SupportBotEvent> Reader { get; }
}

/// <summary>
/// In-memory bounded queue of support events bound for the Telegram-bot HTTP API.
/// Bounded so a submission flood can't exhaust memory; a full queue drops the event
/// (TryEnqueue returns false, the notifier logs a warning) instead of blocking the form —
/// this is the accepted best-effort BOT-001 mode, and the email path stays the durable
/// channel. In-memory: events still queued at shutdown are lost, durable delivery is a
/// separate backlog item of the bot project (BOT-002).
/// </summary>
public sealed class SupportBotEventQueue : ISupportBotEventQueue
{
    public const int Capacity = 500;

    private readonly Channel<SupportBotEvent> _channel =
        Channel.CreateBounded<SupportBotEvent>(new BoundedChannelOptions(Capacity)
        {
            // Wait + TryWrite: never blocks, returns false when full so the drop is visible to the caller.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(SupportBotEvent supportEvent) => _channel.Writer.TryWrite(supportEvent);

    public ChannelReader<SupportBotEvent> Reader => _channel.Reader;
}
