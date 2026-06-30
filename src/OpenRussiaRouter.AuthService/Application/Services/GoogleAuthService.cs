using Microsoft.Extensions.Logging;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class GoogleAuthService(
    IAuthRepository repository,
    ISecretGenerator secretGenerator,
    IProductAnalytics? productAnalytics = null,
    ILogger<GoogleAuthService>? logger = null)
{
    private const string SessionPrefix = "orr_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public async Task<LoginResult> LoginOrRegisterWithGoogleAsync(
        string googleSub,
        string email,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var externalLogin = await repository.GetExternalLoginAsync("google", googleSub, ct);
        UserAccount user;
        // Captured only on the brand-new-account branch so that the analytics event fires
        // exactly once per real Google signup and never for a returning login or for merely
        // linking an external login to an already-existing email account.
        string? newlyCreatedUserId = null;

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
                    nowUtc);
                await repository.AddExternalLoginAsync(link, ct);
                user = byEmail;
            }
            else
            {
                var newUser = UserAccount.CreateForExternalLogin(
                    Guid.NewGuid().ToString("N"),
                    email,
                    UserRole.User,
                    nowUtc);
                var newLogin = new ExternalLogin(
                    Guid.NewGuid().ToString("N"),
                    newUser.Id,
                    "google",
                    googleSub,
                    nowUtc);
                await repository.AddUserWithExternalLoginAsync(newUser, newLogin, ct);
                user = newUser;
                newlyCreatedUserId = newUser.Id;
            }
        }

        var token = secretGenerator.Generate(SessionPrefix, 32);
        var session = new UserSession(token, user.Id, user.Role, nowUtc, nowUtc.Add(SessionLifetime));

        // Emitted after the business operation (account creation + session) has succeeded,
        // fire-and-forget and fail-open: an analytics failure must never break login.
        if (newlyCreatedUserId is not null)
        {
            EmitAnalytics(
                AnalyticsEvents.UserVerified,
                () => productAnalytics!.UserVerified(newlyCreatedUserId, "google"));
        }

        return LoginResult.Success(session);
    }

    private void EmitAnalytics(string eventName, Action emit)
    {
        if (productAnalytics is null)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                emit();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Analytics emission failed for event {EventName}.", eventName);
            }
        });
    }
}
