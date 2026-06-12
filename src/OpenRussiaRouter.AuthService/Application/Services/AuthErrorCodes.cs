namespace OpenRussiaRouter.AuthService.Application.Services;

public static class AuthErrorCodes
{

    public const string InvalidCredentials = "invalid_credentials";

    public const string UserDisabled = "user_disabled";

    public const string InviteAlreadyUsed = "invite_already_used";

    public const string InviteExpired = "invite_expired";

    public const string InviteInvalid = "invite_invalid";

    public const string EmailAlreadyRegistered = "email_already_registered";

    public const string WeakPassword = "weak_password";

    public const string EmailNotVerified = "email_not_verified";

    public const string InvalidCode = "invalid_code";

    public const string CodeExpired = "code_expired";

    public const string TooManyAttempts = "too_many_attempts";

    public const string ResendCooldown = "resend_cooldown";

    public const string NoPendingVerification = "no_pending_verification";

    public const string ApiKeyLimitReached = "api_key_limit_reached";

    public const string InvalidEmail = "invalid_email";

    public const string AccountLocked = "account_locked";

    public const string PasswordResetNotAvailable = "password_reset_not_available";

    public const string NoPendingReset = "no_pending_reset";
}
