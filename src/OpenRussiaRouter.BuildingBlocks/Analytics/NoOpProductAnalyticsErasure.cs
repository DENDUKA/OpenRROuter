namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// No-op erasure used when analytics is disabled or the connection string is missing.
/// Always returns <c>0</c> and performs no I/O.
/// </summary>
public sealed class NoOpProductAnalyticsErasure : IProductAnalyticsErasure
{
    public Task<int> PurgeUserAsync(string userId, CancellationToken ct = default)
    {
        return Task.FromResult(0);
    }
}
