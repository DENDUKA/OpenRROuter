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

public sealed record LoginResult(bool Succeeded, UserSession? Session, string? ErrorCode)
{

    public static LoginResult Success(UserSession session) => new(true, session, null);

    public static LoginResult Failed(string errorCode) => new(false, null, errorCode);
}

