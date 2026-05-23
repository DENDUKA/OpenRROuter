using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages;

public class LoginModel : PageModelBase
{
    private readonly AuthClient _authClient;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? ErrorMessage { get; set; }

    public string? ReturnUrl { get; set; }

    public LoginModel(AuthClient authClient) => _authClient = authClient;

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectTo(User.FindFirstValue(ClaimTypes.Role));

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            var response = await _authClient.LoginAsync(Input.Email, Input.Password);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, response.UserId),
                new Claim(ClaimTypes.Email, response.Email),
                new Claim(ClaimTypes.Role, response.Role),
                new Claim("session_token", response.SessionToken)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RedirectTo(response.Role);
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "invalid_credentials" => "Invalid email or password.",
                "user_disabled" => "Your account has been disabled.",
                _ => "Login failed. Please try again."
            };
        }
        catch
        {
            ErrorMessage = "An error occurred. Please try again.";
        }

        return Page();
    }

    public IActionResult OnPostGoogle()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectTo(User.FindFirstValue(ClaimTypes.Role));

        var props = new AuthenticationProperties { RedirectUri = "/Login/ExternalCallback" };
        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    private IActionResult RedirectTo(string? role) =>
        role == "Admin" ? RedirectToPage("/Admin/Index") : RedirectToPage("/Workspace/ApiKeys");

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";
    }
}
