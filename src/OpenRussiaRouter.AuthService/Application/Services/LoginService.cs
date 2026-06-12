using OpenRussiaRouter.AuthService.Application.Abstractions;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class LoginService(IPasswordHasher passwordHasher, ISecretGenerator secretGenerator)
{
    private const string SessionPrefix = "orr_session";

    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Verifies credentials and applies the brute-force lockout policy. Failed
    /// password attempts and lockout state are mutated on <paramref name="user"/>;
    /// the caller persists them (see <c>IAuthRepository.UpdateLoginThrottleAsync</c>).
    /// </summary>
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

        if (user.IsLockedOut(nowUtc))
        {
            return LoginResult.Failed(AuthErrorCodes.AccountLocked);
        }

        if (user.PasswordHash is null)
        {
            return LoginResult.Failed(AuthErrorCodes.InvalidCredentials);
        }

        if (!passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            var locked = user.RegisterFailedLogin(nowUtc, MaxFailedAttempts, LockoutDuration);
            return LoginResult.Failed(locked ? AuthErrorCodes.AccountLocked : AuthErrorCodes.InvalidCredentials);
        }

        user.ResetLoginThrottle();
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

