namespace OpenRussiaRouter.WebApp.Services.State;

public sealed class AuthState : IAuthState
{
    private UserSession? _session;

    public UserSession? CurrentSession => _session;
    public bool IsAuthenticated => _session is not null;

    public event Action? SessionChanged;

    public void SetSession(UserSession session)
    {
        _session = session;
        SessionChanged?.Invoke();
    }

    public void ClearSession()
    {
        _session = null;
        SessionChanged?.Invoke();
    }
}