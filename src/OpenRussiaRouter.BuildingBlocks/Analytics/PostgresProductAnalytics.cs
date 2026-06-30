using Microsoft.Extensions.Logging;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Postgres-backed product analytics sink. Events are pseudonymized and filtered by
/// <see cref="AnalyticsEventBuilder"/>, then staged in an in-memory bounded channel.
/// The worker (<see cref="ProductAnalyticsFlushWorker"/>) batches them into the
/// analytics database. The sink is fail-open: no method throws.
/// </summary>
public sealed class PostgresProductAnalytics : IProductAnalytics
{
    private readonly AnalyticsEventBuilder _builder;
    private readonly ProductAnalyticsEventQueue _queue;
    private readonly ILogger<PostgresProductAnalytics> _logger;

    public PostgresProductAnalytics(
        AnalyticsEventBuilder builder,
        ProductAnalyticsEventQueue queue,
        ILogger<PostgresProductAnalytics> logger)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void UserVerified(string userId, string signupMethod)
    {
        Enqueue(_builder.BuildUserVerified(userId, signupMethod), dedupKey: null);
    }

    public void ApiKeyCreated(string userId)
    {
        Enqueue(_builder.BuildApiKeyCreated(userId), dedupKey: null);
    }

    public void ApiRequestCompleted(string userId, ApiRequestAnalytics data)
    {
        Enqueue(_builder.BuildApiRequestCompleted(userId, data), data.DedupKey);
    }

    public void PaymentCompleted(string userId, PaymentAnalytics data)
    {
        Enqueue(_builder.BuildPaymentCompleted(userId, data), data.DedupKey);
    }

    private void Enqueue(AnalyticsEvent evt, string? dedupKey)
    {
        try
        {
            var queued = new QueuedAnalyticsEvent(evt, dedupKey, DateTimeOffset.UtcNow);
            if (!_queue.Writer.TryWrite(queued))
            {
                _logger.LogWarning(
                    "Product analytics event queue is full; dropping event {EventName}",
                    evt.EventName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to enqueue analytics event {EventName}",
                evt.EventName);
        }
    }
}
