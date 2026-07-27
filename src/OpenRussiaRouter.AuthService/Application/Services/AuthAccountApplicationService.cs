using Microsoft.Extensions.Logging;
using OpenRussiaRouter.AuthService.Application.Abstractions;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class AuthAccountApplicationService(
    IAuthRepository repository,
    InviteRegistrationService inviteRegistration,
    EmailVerificationService emailVerification,
    IPasswordHasher passwordHasher,
    ApiKeyFactory apiKeyFactory,
    ISecretHasher secretHasher,
    IProductAnalytics? productAnalytics = null,
    ILogger<AuthAccountApplicationService>? logger = null)
{
    private const int DefaultPageSize = 1000;
    private const int MinPasswordLength = 8;

    public async Task<CreatedInvite> CreateInviteAsync(
        string email,
        UserRole role,
        string createdByAdminId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var created = inviteRegistration.CreateInvite(email, role, createdByAdminId, expiresAtUtc, nowUtc);
        await repository.AddInviteAsync(created.Invite, ct).ConfigureAwait(false);
        return created;
    }

    public async Task<UserAccount> CompleteInviteAsync(string rawToken, string password, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var tokenHash = secretHasher.Hash(rawToken);
        var invite = await repository.GetInviteByTokenHashAsync(tokenHash, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Invite token is invalid.");
        var user = inviteRegistration.CompleteInvite(invite, rawToken, password, nowUtc);
        await repository.AddUserAndMarkInviteUsedAsync(user, invite, ct).ConfigureAwait(false);
        return user;
    }

    /// <summary>
    /// Self-serve signup. Creates a <see cref="UserStatus.PendingVerification"/>
    /// account and a 6-digit email-confirmation code. The raw code is returned so
    /// the caller (WebApp) can email it; only its hash is persisted.
    /// </summary>
    public async Task<RegisterResult> RegisterUserAsync(string email, string password, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
        {
            return RegisterResult.Failed(AuthErrorCodes.WeakPassword);
        }

        string normalizedEmail;
        try
        {
            normalizedEmail = EmailValidator.Normalize(email);
        }
        catch (ArgumentException)
        {
            return RegisterResult.Failed(AuthErrorCodes.InvalidEmail);
        }

        if (await repository.GetUserByEmailAsync(normalizedEmail, ct).ConfigureAwait(false) is not null)
        {
            return RegisterResult.Failed(AuthErrorCodes.EmailAlreadyRegistered);
        }

        var user = UserAccount.CreateForEmailSignup(
            Guid.NewGuid().ToString("N"),
            normalizedEmail,
            UserRole.User,
            passwordHasher.HashPassword(password),
            nowUtc);

        var issued = emailVerification.Create(user.Id, nowUtc);
        await repository.AddUserWithEmailVerificationAsync(user, issued.Verification, ct).ConfigureAwait(false);

        return RegisterResult.Success(user.Id, user.Email, issued.RawCode);
    }

    /// <summary>Confirms a pending signup's email; on success activates the account.</summary>
    public async Task<VerifyEmailResult> VerifyEmailAsync(string email, string code, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var user = await repository.GetUserByEmailAsync(email, ct).ConfigureAwait(false);
        if (user is not { Status: UserStatus.PendingVerification })
        {
            return VerifyEmailResult.Failed(AuthErrorCodes.NoPendingVerification);
        }

        var verification = await repository.GetEmailVerificationAsync(user.Id, ct).ConfigureAwait(false);
        if (verification is null)
        {
            return VerifyEmailResult.Failed(AuthErrorCodes.NoPendingVerification);
        }

        switch (emailVerification.Check(verification, code, nowUtc))
        {
            case VerificationCheck.Confirmed:
                await repository.ActivateUserAndDeleteVerificationAsync(user.Id, ct).ConfigureAwait(false);
                user.Activate();
                EmitAnalytics(
                    AnalyticsEvents.UserVerified,
                    () => productAnalytics!.UserVerified(user.Id, "email"));
                return VerifyEmailResult.Success(user);

            case VerificationCheck.Expired:
                return VerifyEmailResult.Failed(AuthErrorCodes.CodeExpired);

            case VerificationCheck.TooManyAttempts:
                await repository.UpdateEmailVerificationAsync(verification, ct).ConfigureAwait(false);
                return VerifyEmailResult.Failed(AuthErrorCodes.TooManyAttempts);

            default: // WrongCode
                await repository.UpdateEmailVerificationAsync(verification, ct).ConfigureAwait(false);
                return VerifyEmailResult.Failed(AuthErrorCodes.InvalidCode);
        }
    }

    /// <summary>Re-issues a verification code for a pending signup (subject to a resend cooldown).</summary>
    public async Task<ResendVerificationResult> ResendVerificationAsync(string email, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var user = await repository.GetUserByEmailAsync(email, ct).ConfigureAwait(false);
        if (user is not { Status: UserStatus.PendingVerification })
        {
            return ResendVerificationResult.Failed(AuthErrorCodes.NoPendingVerification);
        }

        var verification = await repository.GetEmailVerificationAsync(user.Id, ct).ConfigureAwait(false);
        if (verification is null)
        {
            return ResendVerificationResult.Failed(AuthErrorCodes.NoPendingVerification);
        }

        var code = emailVerification.Reissue(verification, nowUtc);
        if (code is null)
        {
            return ResendVerificationResult.Failed(AuthErrorCodes.ResendCooldown);
        }

        await repository.UpdateEmailVerificationAsync(verification, ct).ConfigureAwait(false);
        return ResendVerificationResult.Success(user.Email, code);
    }

    /// <summary>
    /// Starts (or re-issues, subject to the resend cooldown) a password reset for an
    /// account. The raw 6-digit code is returned so the caller can email it; only its
    /// hash is persisted. Unknown emails and accounts without a password (external
    /// login only) fail with <see cref="AuthErrorCodes.PasswordResetNotAvailable"/> —
    /// the WebApp shows the same generic message either way, so account existence is
    /// not disclosed.
    /// </summary>
    public async Task<PasswordResetRequestResult> RequestPasswordResetAsync(string email, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        string normalizedEmail;
        try
        {
            normalizedEmail = EmailValidator.Normalize(email);
        }
        catch (ArgumentException)
        {
            return PasswordResetRequestResult.Failed(AuthErrorCodes.PasswordResetNotAvailable);
        }

        var user = await repository.GetUserByEmailAsync(normalizedEmail, ct).ConfigureAwait(false);
        if (user is not { Status: UserStatus.Active } || user.PasswordHash is null)
        {
            return PasswordResetRequestResult.Failed(AuthErrorCodes.PasswordResetNotAvailable);
        }

        var existing = await repository.GetPasswordResetAsync(user.Id, ct).ConfigureAwait(false);
        if (existing is not null && !existing.IsExpired(nowUtc))
        {
            var reissued = emailVerification.Reissue(existing, nowUtc);
            if (reissued is null)
            {
                return PasswordResetRequestResult.Failed(AuthErrorCodes.ResendCooldown);
            }

            await repository.UpdatePasswordResetAsync(existing, ct).ConfigureAwait(false);
            return PasswordResetRequestResult.Success(user.Email, reissued);
        }

        var issued = emailVerification.Create(user.Id, nowUtc);
        await repository.UpsertPasswordResetAsync(issued.Verification, ct).ConfigureAwait(false);
        return PasswordResetRequestResult.Success(user.Email, issued.RawCode);
    }

    /// <summary>
    /// Completes a password reset: checks the emailed code and, on success, stores the
    /// new password hash, clears the login lockout and deletes the pending reset.
    /// </summary>
    public async Task<PasswordChangeResult> ResetPasswordAsync(string email, string code, string newPassword, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinPasswordLength)
        {
            return PasswordChangeResult.Failed(AuthErrorCodes.WeakPassword);
        }

        var user = await repository.GetUserByEmailAsync(email, ct).ConfigureAwait(false);
        if (user is null)
        {
            return PasswordChangeResult.Failed(AuthErrorCodes.NoPendingReset);
        }

        var reset = await repository.GetPasswordResetAsync(user.Id, ct).ConfigureAwait(false);
        if (reset is null)
        {
            return PasswordChangeResult.Failed(AuthErrorCodes.NoPendingReset);
        }

        switch (emailVerification.Check(reset, code, nowUtc))
        {
            case VerificationCheck.Confirmed:
                await repository.UpdatePasswordAndDeleteResetAsync(user.Id, passwordHasher.HashPassword(newPassword), ct).ConfigureAwait(false);
                return PasswordChangeResult.Success();

            case VerificationCheck.Expired:
                return PasswordChangeResult.Failed(AuthErrorCodes.CodeExpired);

            case VerificationCheck.TooManyAttempts:
                await repository.UpdatePasswordResetAsync(reset, ct).ConfigureAwait(false);
                return PasswordChangeResult.Failed(AuthErrorCodes.TooManyAttempts);

            default: // WrongCode
                await repository.UpdatePasswordResetAsync(reset, ct).ConfigureAwait(false);
                return PasswordChangeResult.Failed(AuthErrorCodes.InvalidCode);
        }
    }

    /// <summary>Changes the password of a logged-in user after verifying the current one.</summary>
    public async Task<PasswordChangeResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinPasswordLength)
        {
            return PasswordChangeResult.Failed(AuthErrorCodes.WeakPassword);
        }

        var user = await repository.GetUserAsync(Preconditions.RequireValue(userId, nameof(userId)), ct).ConfigureAwait(false);
        if (user is not { Status: UserStatus.Active })
        {
            return PasswordChangeResult.Failed(AuthErrorCodes.UserDisabled);
        }

        if (user.PasswordHash is null || !passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            return PasswordChangeResult.Failed(AuthErrorCodes.InvalidCredentials);
        }

        await repository.UpdatePasswordHashAsync(user.Id, passwordHasher.HashPassword(newPassword), ct).ConfigureAwait(false);
        return PasswordChangeResult.Success();
    }

    public async Task UpdateUserAsync(string userId, string? displayName, int maxApiKeys, string? displayCurrency = null, CancellationToken ct = default)
    {
        var user = await repository.GetUserAsync(userId, ct).ConfigureAwait(false);
        if (user == null)
            throw new InvalidOperationException("User not found.");

        user.UpdateDetails(displayName, maxApiKeys, displayCurrency);
        await repository.UpdateUserAsync(user, ct).ConfigureAwait(false);
    }

    public async Task<CreateApiKeyResult> CreateApiKeyAsync(string userId, string name, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var user = await repository.GetUserAsync(userId, ct).ConfigureAwait(false);
        if (user is not { Status: UserStatus.Active })
            return CreateApiKeyResult.Failed(AuthErrorCodes.UserDisabled);

        var currentKeysCount = await repository.CountApiKeysAsync(userId, ct).ConfigureAwait(false);
        if (currentKeysCount >= user.MaxApiKeys)
        {
            return CreateApiKeyResult.Failed(AuthErrorCodes.ApiKeyLimitReached);
        }

        var created = apiKeyFactory.Create(userId, name, nowUtc);
        await repository.AddApiKeyAsync(created.ApiKey, ct).ConfigureAwait(false);
        EmitAnalytics(
            AnalyticsEvents.ApiKeyCreated,
            () => productAnalytics!.ApiKeyCreated(userId));
        return CreateApiKeyResult.Success(created);
    }

    public async Task RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default)
    {
        if (!await repository.RenameApiKeyAsync(
                Preconditions.RequireValue(userId, nameof(userId)),
                Preconditions.RequireValue(apiKeyId, nameof(apiKeyId)),
                Preconditions.RequireValue(name, nameof(name)),
                ct).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("API key was not found.");
        }
    }

    public async Task DisableApiKeyAsync(string userId, string apiKeyId, DateTimeOffset disabledAtUtc, CancellationToken ct = default)
    {
        Preconditions.RequireValue(userId, nameof(userId));
        Preconditions.RequireValue(apiKeyId, nameof(apiKeyId));

        if (!await repository.DisableApiKeyAsync(userId, apiKeyId, disabledAtUtc, ct).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("API key was not found or already disabled.");
        }
    }

    public async Task EnableApiKeyAsync(string userId, string apiKeyId, CancellationToken ct = default)
    {
        Preconditions.RequireValue(userId, nameof(userId));
        Preconditions.RequireValue(apiKeyId, nameof(apiKeyId));

        if (!await repository.EnableApiKeyAsync(userId, apiKeyId, ct).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("API key was not found or already enabled.");
        }
    }

    public async Task DeleteApiKeyAsync(string userId, string apiKeyId, DateTimeOffset deletedAtUtc, CancellationToken ct = default)
    {
        Preconditions.RequireValue(userId, nameof(userId));
        Preconditions.RequireValue(apiKeyId, nameof(apiKeyId));

        if (!await repository.DeleteApiKeyAsync(
                userId,
                apiKeyId,
                deletedAtUtc,
                ct).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("API key was not found or is not disabled.");
        }
    }

    public async Task<IReadOnlyCollection<ApiKey>> ListApiKeysAsync(string userId, bool includeDeleted = false, CancellationToken ct = default)
    {
        return (await repository.ListApiKeysAsync(
            Preconditions.RequireValue(userId, nameof(userId)),
            DefaultPageSize,
            null,
            includeDeleted,
            ct).ConfigureAwait(false)).Items;
    }

    public async Task<IReadOnlyCollection<UserAccount>> ListUsersAsync(CancellationToken ct = default)
    {
        return (await repository.ListUsersAsync(DefaultPageSize, null, ct).ConfigureAwait(false)).Items;
    }

    private void EmitAnalytics(string eventName, Action emit)
        => productAnalytics.EmitSafe(logger, eventName, emit);
}

public sealed record RegisterResult(bool Succeeded, string? UserId, string? Email, string? RawCode, string? ErrorCode)
{
    public static RegisterResult Success(string userId, string email, string rawCode) => new(true, userId, email, rawCode, null);

    public static RegisterResult Failed(string errorCode) => new(false, null, null, null, errorCode);
}

public sealed record VerifyEmailResult(bool Succeeded, UserAccount? User, string? ErrorCode)
{
    public static VerifyEmailResult Success(UserAccount user) => new(true, user, null);

    public static VerifyEmailResult Failed(string errorCode) => new(false, null, errorCode);
}

public sealed record ResendVerificationResult(bool Succeeded, string? Email, string? RawCode, string? ErrorCode)
{
    public static ResendVerificationResult Success(string email, string rawCode) => new(true, email, rawCode, null);

    public static ResendVerificationResult Failed(string errorCode) => new(false, null, null, errorCode);
}

public sealed record PasswordResetRequestResult(bool Succeeded, string? Email, string? RawCode, string? ErrorCode)
{
    public static PasswordResetRequestResult Success(string email, string rawCode) => new(true, email, rawCode, null);

    public static PasswordResetRequestResult Failed(string errorCode) => new(false, null, null, errorCode);
}

public sealed record PasswordChangeResult(bool Succeeded, string? ErrorCode)
{
    public static PasswordChangeResult Success() => new(true, null);

    public static PasswordChangeResult Failed(string errorCode) => new(false, errorCode);
}


