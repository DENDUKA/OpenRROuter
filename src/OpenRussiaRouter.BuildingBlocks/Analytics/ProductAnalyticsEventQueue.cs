using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// In-memory bounded queue between <see cref="PostgresProductAnalytics"/> and
/// <see cref="ProductAnalyticsFlushWorker"/>. When full, writes are dropped so
/// analytics can never block or break the business path.
/// </summary>
public sealed class ProductAnalyticsEventQueue
{
    private readonly Channel<QueuedAnalyticsEvent> _channel;

    public ProductAnalyticsEventQueue(IOptions<ProductAnalyticsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var capacity = options.Value.QueueCapacity;
        if (capacity <= 0)
        {
            capacity = 10_000;
        }

        _channel = Channel.CreateBounded<QueuedAnalyticsEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelWriter<QueuedAnalyticsEvent> Writer => _channel.Writer;

    public ChannelReader<QueuedAnalyticsEvent> Reader => _channel.Reader;
}

/// <summary>
/// A single analytics event staged for the Postgres sink. The concrete event payload
/// and the business deduplication key are kept separate so the dedup key never leaks
/// into the stored properties JSON.
/// </summary>
public readonly record struct QueuedAnalyticsEvent(
    AnalyticsEvent Event,
    string? DedupKey,
    DateTimeOffset OccurredAt);
