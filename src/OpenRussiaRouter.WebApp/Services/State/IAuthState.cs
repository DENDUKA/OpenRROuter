namespace OpenRussiaRouter.WebApp.Services.State;

public interface IAuthState
{
    UserSession? CurrentSession { get; }
    bool IsAuthenticated { get; }
    event Action? SessionChanged;
    void SetSession(UserSession session);
    void ClearSession();
}