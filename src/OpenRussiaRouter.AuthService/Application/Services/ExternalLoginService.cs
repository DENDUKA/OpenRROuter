using Microsoft.Extensions.Logging;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class ExternalLoginService(
    IAuthRepository repository,
    ISecretGenerator secretGenerator,
    IProductAnalytics? productAnalytics = null,
    ILogger<ExternalLoginService>? logger = null)
{
    private const string SessionPrefix = "orr_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public async Task<LoginResult> LoginOrRegisterAsync(
        string provider,
        string providerSub,
        string email,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var externalLogin = await repository.GetExternalLoginAsync(provider, providerSub, ct);
        UserAccount user;
        string? newlyCreatedUserId = null;
        string? activatedUserId = null;

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
                if (byEmail.Status == UserStatus.PendingVerification)
                {
                    await repository.ActivateUserAndDeleteVerificationAsync(byEmail.Id, ct).ConfigureAwait(false);
                    byEmail.Activate();
                    activatedUserId = byEmail.Id;
                }
                var link = new ExternalLogin(
                    Guid.NewGuid().ToString("N"),
                    byEmail.Id,
                    provider,
                    providerSub,
                    nowUtc);
                await repository.AddExternalLoginAsync(link, ct).ConfigureAwait(false);
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
                    provider,
                    providerSub,
                    nowUtc);
                await repository.AddUserWithExternalLoginAsync(newUser, newLogin, ct);
                user = newUser;
                newlyCreatedUserId = newUser.Id;
            }
        }

        var token = secretGenerator.Generate(SessionPrefix, 32);
        var session = new UserSession(token, user.Id, user.Role, nowUtc, nowUtc.Add(SessionLifetime));

        if (newlyCreatedUserId is not null)
        {
            EmitAnalytics(
                AnalyticsEvents.UserVerified,
                () => productAnalytics!.UserVerified(newlyCreatedUserId, provider));
        }
        else if (activatedUserId is not null)
        {
            EmitAnalytics(
                AnalyticsEvents.UserVerified,
                () => productAnalytics!.UserVerified(activatedUserId, provider));
        }

        // Same condition as the analytics emission above: only the branch that actually created
        // an account counts as a signup. Linking a provider to an account that registered by
        // password earlier is a login, and reporting it would inflate the funnel.
        return LoginResult.Success(session, isNewUser: newlyCreatedUserId is not null);
    }

    private void EmitAnalytics(string eventName, Action emit)
        => productAnalytics.EmitSafe(logger, eventName, emit);
}
