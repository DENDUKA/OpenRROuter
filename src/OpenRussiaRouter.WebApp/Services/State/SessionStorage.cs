using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Security.Cryptography;

namespace OpenRussiaRouter.WebApp.Services.State;

public sealed class SessionStorage(ProtectedLocalStorage localStorage) : ISessionStorage
{
    private const string SessionKey = "orr_session";

    public async Task SaveSessionAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await localStorage.SetAsync(SessionKey, session);
    }

    public async Task<UserSession?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await localStorage.GetAsync<UserSession>(SessionKey);
            return result.Success ? result.Value : null;
        }
        catch (CryptographicException)
        {
            await ClearSessionAsync(cancellationToken);
            return null;
        }
    }

    public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        await localStorage.DeleteAsync(SessionKey);
    }
}
