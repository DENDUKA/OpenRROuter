using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class LoginService(Pbkdf2PasswordHasher passwordHasher, ISecretGenerator secretGenerator)
{
    private const string SessionPrefix = "orr_session";

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

        if (user.Status == UserStatus.PendingVerification)
        {
            return LoginResult.Failed(AuthErrorCodes.EmailNotVerified);
        }

        if (user.PasswordHash is null)
        {
            return LoginResult.Failed(AuthErrorCodes.InvalidCredentials);
        }

        if (!passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            return LoginResult.Failed(AuthErrorCodes.InvalidCredentials);
        }

        return LoginResult.Success(IssueSession(user, nowUtc, sessionLifetime));
    }

    /// <summary>
    /// Mints a session for an already-authenticated user (e.g. right after a
    /// successful email verification), bypassing the password check.
    /// </summary>
    public UserSession IssueSession(UserAccount user, DateTimeOffset nowUtc, TimeSpan sessionLifetime)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (sessionLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionLifetime), "Session lifetime must be positive.");
        }

        return new UserSession(
            secretGenerator.Generate(SessionPrefix, 32),
            user.Id,
            user.Role,
            nowUtc,
            nowUtc.Add(sessionLifetime));
    }
}

public sealed record LoginResult(bool Succeeded, UserSession? Session, string? ErrorCode)
{

    public static LoginResult Success(UserSession session) => new(true, session, null);

    public static LoginResult Failed(string errorCode) => new(false, null, errorCode);
}

