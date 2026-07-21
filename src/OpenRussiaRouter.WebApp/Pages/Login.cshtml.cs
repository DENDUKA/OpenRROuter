using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using AspNet.Security.OAuth.Yandex;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp;

namespace OpenRussiaRouter.WebApp.Pages;

public class LoginModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<LoginModel> _logger;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? ErrorMessage { get; set; }

    public string? ReturnUrl { get; set; }

    public bool GoogleEnabled { get; }
    public bool YandexEnabled { get; }

    public LoginModel(AuthProvider AuthProvider, IStringLocalizer<SharedResource> localizer, ILogger<LoginModel> logger, IConfiguration configuration)
    {
        _authClient = AuthProvider;
        _localizer = localizer;
        _logger = logger;
        GoogleEnabled = !string.IsNullOrWhiteSpace(configuration["Google:ClientId"])
                     && !string.IsNullOrWhiteSpace(configuration["Google:ClientSecret"]);
        YandexEnabled = !string.IsNullOrWhiteSpace(configuration["Yandex:ClientId"])
                     && !string.IsNullOrWhiteSpace(configuration["Yandex:ClientSecret"]);
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            return RedirectTo(string.IsNullOrEmpty(role) ? "/Workspace" : $"/{role}");
        }

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
            if (ex.ErrorCode == "email_not_verified")
            {
                // Account exists but the email was never confirmed — send them to the
                // verify page (where they can enter or resend the code).
                TempData["StatusMessage"] = _localizer["Auth_EmailNotVerified"].Value;
                return RedirectToPage("/VerifyEmail", new { email = Input.Email });
            }

            ErrorMessage = ex.ErrorCode switch
            {
                "invalid_credentials" => _localizer["Auth_InvalidCredentials"],
                "user_disabled" => _localizer["Auth_UserDisabled"],
                "account_locked" => _localizer["Auth_AccountLocked"],
                _ => _localizer["Auth_LoginFailed"]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login. Email={Email}", Input.Email);
            ErrorMessage = _localizer["Auth_LoginError"];
        }

        return Page();
    }

    public IActionResult OnPostGoogle()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectTo(User.FindFirstValue(ClaimTypes.Role));

        var props = new AuthenticationProperties
        {
            RedirectUri = "/Login/ExternalCallback",
            Items = { ["provider"] = "google" }
        };
        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    public IActionResult OnPostYandex()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectTo(User.FindFirstValue(ClaimTypes.Role));

        var props = new AuthenticationProperties
        {
            RedirectUri = "/Login/ExternalCallback",
            Items = { ["provider"] = "yandex" }
        };
        return Challenge(props, YandexAuthenticationDefaults.AuthenticationScheme);
    }

    private IActionResult RedirectTo(string? role) =>
        role == "Admin" ? RedirectToPage("/Admin/Index") : RedirectToPage("/Workspace/ApiKeys");

    public class InputModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [EmailAddress(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        public string Password { get; set; } = "";
    }
}
