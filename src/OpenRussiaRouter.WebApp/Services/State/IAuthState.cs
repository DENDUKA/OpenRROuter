namespace OpenRussiaRouter.WebApp.Services.State;

public interface IAuthState
{
    UserSession? CurrentSession { get; }
    bool IsAuthenticated { get; }
    bool IsSessionLoaded { get; }
    event Action? SessionChanged;
    event Action? SessionLoaded;
    void SetSession(UserSession session);
    void ClearSession();
    void MarkSessionLoaded();
}