using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// In-memory bounded queue between the site-visit middleware and
/// <see cref="SiteVisitFlushWorker"/>. Mirrors <see cref="ProductAnalyticsEventQueue"/>:
/// when full, writes are dropped so traffic accounting can never slow down or break page
/// rendering.
/// </summary>
public sealed class SiteVisitQueue
{
    private readonly Channel<SiteVisit> _channel;

    public SiteVisitQueue(IOptions<SiteVisitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var capacity = options.Value.QueueCapacity;
        if (capacity <= 0)
        {
            capacity = 10_000;
        }

        _channel = Channel.CreateBounded<SiteVisit>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelWriter<SiteVisit> Writer => _channel.Writer;

    public ChannelReader<SiteVisit> Reader => _channel.Reader;

    /// <summary>Stages a visit; returns <c>false</c> when the queue is full and the visit was dropped.</summary>
    public bool TryEnqueue(SiteVisit visit) => _channel.Writer.TryWrite(visit);
}
