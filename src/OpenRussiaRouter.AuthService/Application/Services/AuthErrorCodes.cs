namespace OpenRussiaRouter.AuthService.Application.Services;

/// <summary>
/// Auth-specific error codes.
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>
    /// Invalid email or password.
    /// </summary>
    public const string InvalidCredentials = "invalid_credentials";

    /// <summary>
    /// User account is disabled.
    /// </summary>
    public const string UserDisabled = "user_disabled";

    /// <summary>
    /// Invite has already been used.
    /// </summary>
    public const string InviteAlreadyUsed = "invite_already_used";

    /// <summary>
    /// Invite has expired.
    /// </summary>
    public const string InviteExpired = "invite_expired";

    /// <summary>
    /// Invite token is invalid.
    /// </summary>
    public const string InviteInvalid = "invite_invalid";
}
