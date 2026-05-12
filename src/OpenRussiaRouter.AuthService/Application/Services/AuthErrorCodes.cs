namespace OpenRussiaRouter.AuthService.Application.Services;

public static class AuthErrorCodes
{

    public const string InvalidCredentials = "invalid_credentials";

    public const string UserDisabled = "user_disabled";

    public const string InviteAlreadyUsed = "invite_already_used";

    public const string InviteExpired = "invite_expired";

    public const string InviteInvalid = "invite_invalid";
}
