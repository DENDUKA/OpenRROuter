namespace OpenRussiaRouter.WebApp.Services.State;

public interface ISessionStorage
{
    Task SaveSessionAsync(UserSession session, CancellationToken cancellationToken = default);
    Task<UserSession?> LoadSessionAsync(CancellationToken cancellationToken = default);
    Task ClearSessionAsync(CancellationToken cancellationToken = default);
}