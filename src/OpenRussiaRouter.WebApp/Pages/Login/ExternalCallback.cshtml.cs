using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages.Login;

public sealed class ExternalCallbackModel(AuthClient authClient) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var result = await HttpContext.AuthenticateAsync("External");
        if (!result.Succeeded)
            return RedirectToPage("/Login");

        var googleSub = result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = result.Principal!.FindFirstValue(ClaimTypes.Email);

        if (googleSub is null || email is null)
            return RedirectToPage("/Login");

        var loginResult = await authClient.LoginOrRegisterWithGoogleAsync(googleSub, email);
        if (loginResult is null)
            return RedirectToPage("/Login");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, loginResult.UserId),
            new Claim(ClaimTypes.Email, loginResult.Email),
            new Claim(ClaimTypes.Role, loginResult.Role),
            new Claim("session_token", loginResult.SessionToken),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
        await HttpContext.SignOutAsync("External");

        return loginResult.Role == "Admin"
            ? RedirectToPage("/Admin/Index")
            : RedirectToPage("/Workspace/ApiKeys");
    }
}
