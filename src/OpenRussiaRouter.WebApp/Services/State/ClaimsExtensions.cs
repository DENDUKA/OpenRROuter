using System.Security.Claims;

namespace OpenRussiaRouter.WebApp.Services.State;

public static class ClaimsExtensions
{
    public static UserSession? GetUserSession(this ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return null;

        return new UserSession
        {
            UserId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
            Email = principal.FindFirstValue(ClaimTypes.Email) ?? "",
            Role = principal.FindFirstValue(ClaimTypes.Role) ?? "",
            SessionToken = principal.FindFirstValue("session_token") ?? ""
        };
    }

    public static string GetUserId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    public static string GetSessionToken(this ClaimsPrincipal principal)
        => principal.FindFirstValue("session_token") ?? "";
}
