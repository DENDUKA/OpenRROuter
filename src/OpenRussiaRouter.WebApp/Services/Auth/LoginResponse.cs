namespace OpenRussiaRouter.WebApp.Services.Auth;

/// <param name="IsNewUser">
/// Set only by the external-provider login when it created the account. Password sign-in and email
/// verification leave it false — those paths report their own conversions.
/// </param>
public sealed record LoginResponse(
    string SessionToken,
    string UserId,
    string Role,
    string Email,
    bool IsNewUser = false);
