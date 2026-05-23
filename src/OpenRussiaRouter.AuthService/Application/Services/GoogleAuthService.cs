using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class GoogleAuthService(IAuthRepository repository, ISecretGenerator secretGenerator)
{
    private const string SessionPrefix = "orr_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public async Task<LoginResult> LoginOrRegisterWithGoogleAsync(
        string googleSub,
        string email,
        CancellationToken ct = default)
    {
        var externalLogin = await repository.GetExternalLoginAsync("google", googleSub, ct);
        UserAccount user;

        if (externalLogin is not null)
        {
            var existing = await repository.GetUserAsync(externalLogin.UserId, ct);
            if (existing is null || existing.Status == UserStatus.Disabled)
                return LoginResult.Failed(AuthErrorCodes.UserDisabled);
            user = existing;
        }
        else
        {
            var byEmail = await repository.GetUserByEmailAsync(email, ct);
            if (byEmail is not null)
            {
                if (byEmail.Status == UserStatus.Disabled)
                    return LoginResult.Failed(AuthErrorCodes.UserDisabled);
                var link = new ExternalLogin(
                    Guid.NewGuid().ToString("N"),
                    byEmail.Id,
                    "google",
                    googleSub,
                    DateTimeOffset.UtcNow);
                await repository.AddExternalLoginAsync(link, ct);
                user = byEmail;
            }
            else
            {
                var newUser = UserAccount.CreateForExternalLogin(
                    Guid.NewGuid().ToString("N"),
                    email,
                    UserRole.User,
                    DateTimeOffset.UtcNow);
                var newLogin = new ExternalLogin(
                    Guid.NewGuid().ToString("N"),
                    newUser.Id,
                    "google",
                    googleSub,
                    DateTimeOffset.UtcNow);
                await repository.AddUserWithExternalLoginAsync(newUser, newLogin, ct);
                user = newUser;
            }
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var token = secretGenerator.Generate(SessionPrefix, 32);
        var session = new UserSession(token, user.Id, user.Role, nowUtc, nowUtc.Add(SessionLifetime));
        return LoginResult.Success(session);
    }
}
