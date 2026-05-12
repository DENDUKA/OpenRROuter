using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

/// <summary>
/// Handles email/password login and session creation.
/// </summary>
public sealed class LoginService(Pbkdf2PasswordHasher passwordHasher, ISecretGenerator secretGenerator)
{
    private const string SessionPrefix = "orr_session";

    /// <summary>
    /// Attempts to authenticate a user.
    /// </summary>
    /// <param name="user">User account found by normalized email.</param>
    /// <param name="password">Raw password.</param>
    /// <param name="nowUtc">Current timestamp.</param>
    /// <param name="sessionLifetime">Session lifetime.</param>
    /// <returns>Login result with a session on success.</returns>
    public LoginResult Login(
        UserAccount? user,
        string password,
        DateTimeOffset nowUtc,
        TimeSpan sessionLifetime)
    {
        if (sessionLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionLifetime), "Session lifetime must be positive.");
        }

        if (user is null)
        {
            return LoginResult.Failed(AuthErrorCodes.InvalidCredentials);
        }

        if (user.Status == UserStatus.Disabled)
        {
            return LoginResult.Failed(AuthErrorCodes.UserDisabled);
        }

        if (!passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            return LoginResult.Failed(AuthErrorCodes.InvalidCredentials);
        }

        var session = new UserSession(
            secretGenerator.Generate(SessionPrefix, 32),
            user.Id,
            user.Role,
            nowUtc,
            nowUtc.Add(sessionLifetime));

        return LoginResult.Success(session);
    }
}

/// <summary>
/// Email/password login result.
/// </summary>
/// <param name="Succeeded">Whether login succeeded.</param>
/// <param name="Session">Created session for successful login.</param>
/// <param name="ErrorCode">Machine-readable error code for failed login.</param>
public sealed record LoginResult(bool Succeeded, UserSession? Session, string? ErrorCode)
{
    /// <summary>
    /// Creates a successful login result.
    /// </summary>
    /// <param name="session">Created user session.</param>
    /// <returns>Successful login result.</returns>
    public static LoginResult Success(UserSession session) => new(true, session, null);

    /// <summary>
    /// Creates a failed login result.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code.</param>
    /// <returns>Failed login result.</returns>
    public static LoginResult Failed(string errorCode) => new(false, null, errorCode);
}

