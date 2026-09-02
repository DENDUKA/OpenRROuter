namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Capability to erase all analytics-owned data for a specific user. Separate from the
/// hot-path <see cref="IProductAnalytics"/> interface so that erasure concerns do not
/// leak into the event emission surface.
/// </summary>
public interface IProductAnalyticsErasure
{
    /// <summary>
    /// Deletes product events and the Yandex Metrica identity mapping for
    /// <paramref name="userId"/>.
    /// </summary>
    /// <param name="userId">Raw user identifier (not logged or stored).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows deleted.</returns>
    Task<int> PurgeUserAsync(string userId, CancellationToken ct = default);
}
