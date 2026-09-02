namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>Stores the relationship between an ORR user and a Yandex Metrica ClientID.</summary>
public interface IYandexMetrikaIdentityStore
{
    /// <summary>Creates or refreshes the ClientID observed for a user.</summary>
    Task UpsertAsync(
        string userId,
        string clientId,
        DateTimeOffset observedAtUtc,
        CancellationToken ct = default);

    /// <summary>Returns the most recently observed ClientID for a user, if available.</summary>
    Task<string?> GetClientIdAsync(string userId, CancellationToken ct = default);
}
