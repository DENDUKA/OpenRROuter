using Microsoft.Extensions.Logging;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Fire-and-forget helper for product analytics emissions.
/// Fail-open: analytics failures are logged and swallowed, never breaking the business path.
/// </summary>
public static class ProductAnalyticsExtensions
{
    /// <summary>
    /// Emits an analytics event in a background <see cref="Task"/>.
    /// Exceptions are caught and logged as warnings — analytics failures must never break operations.
    /// </summary>
    /// <param name="analytics">The analytics instance (nullable — null is a no-op).</param>
    /// <param name="logger">Logger used to report emission failures (nullable — null disables logging).</param>
    /// <param name="eventName">Event name for the log message.</param>
    /// <param name="emit">Delegate that performs the actual emission.</param>
    public static void EmitSafe(
        this IProductAnalytics? analytics,
        ILogger? logger,
        string eventName,
        Action emit)
    {
        if (analytics is null)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                emit();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Analytics emission failed for event {EventName}.", eventName);
            }
        });
    }
}
