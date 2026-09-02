namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>Disabled Yandex Metrica identity store.</summary>
public sealed class NoOpYandexMetrikaIdentityStore : IYandexMetrikaIdentityStore
{
    /// <inheritdoc />
    public Task UpsertAsync(
        string userId,
        string clientId,
        DateTimeOffset observedAtUtc,
        CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<string?> GetClientIdAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
