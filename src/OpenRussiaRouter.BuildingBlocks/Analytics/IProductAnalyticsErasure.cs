namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Capability to erase all analytics events for a specific user. Separate from the
/// hot-path <see cref="IProductAnalytics"/> interface so that erasure concerns do not
/// leak into the event emission surface.
/// </summary>
public interface IProductAnalyticsErasure
{
    /// <summary>
    /// Deletes every row in <c>analytics.events</c> whose <c>distinct_id</c> matches
    /// the HMAC-pseudonymized identifier for <paramref name="userId"/>.
    /// </summary>
    /// <param name="userId">Raw user identifier (not logged or stored).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows deleted.</returns>
    Task<int> PurgeUserAsync(string userId, CancellationToken ct = default);
}
