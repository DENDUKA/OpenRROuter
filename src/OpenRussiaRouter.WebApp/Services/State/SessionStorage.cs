using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace OpenRussiaRouter.WebApp.Services.State;

public interface ISessionStorage
{
    Task SaveSessionAsync(UserSession session, CancellationToken cancellationToken = default);
    Task<UserSession?> LoadSessionAsync(CancellationToken cancellationToken = default);
    Task ClearSessionAsync(CancellationToken cancellationToken = default);
}

public sealed class SessionStorage(ProtectedLocalStorage localStorage) : ISessionStorage
{
    private const string SessionKey = "orr_session";

    public async Task SaveSessionAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await localStorage.SetAsync(SessionKey, session);
    }

    public async Task<UserSession?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        var result = await localStorage.GetAsync<UserSession>(SessionKey);
        return result.Success ? result.Value : null;
    }

    public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        await localStorage.DeleteAsync(SessionKey);
    }
}