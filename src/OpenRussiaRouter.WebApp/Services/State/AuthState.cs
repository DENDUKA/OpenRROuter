namespace OpenRussiaRouter.WebApp.Services.State;

public sealed class UserSession
{
    public required string SessionToken { get; init; }
    public required string UserId { get; init; }
    public required string Role { get; init; }
    public bool IsAdmin => Role == "Admin";
}

public interface IAuthState
{
    UserSession? CurrentSession { get; }
    bool IsAuthenticated { get; }
    event Action? SessionChanged;
    void SetSession(UserSession session);
    void ClearSession();
}

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