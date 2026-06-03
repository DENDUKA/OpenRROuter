using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages;

public class VerifyEmailModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly IEmailDispatchQueue _emailQueue;
    private readonly ILogger<VerifyEmailModel> _logger;

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code.")]
    public string Code { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    public VerifyEmailModel(AuthProvider authProvider, IEmailDispatchQueue emailQueue, ILogger<VerifyEmailModel> logger)
    {
        _authClient = authProvider;
        _emailQueue = emailQueue;
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Workspace/ApiKeys");

        StatusMessage = TempData["StatusMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Missing email. Please sign up again.";
            return Page();
        }
        if (!ModelState.IsValid) return Page();

        try
        {
            var response = await _authClient.VerifyEmailAsync(Email, Code);

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

            return RedirectToPage("/Workspace/ApiKeys");
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "invalid_code" => "Incorrect code. Please check and try again.",
                "code_expired" => "That code has expired. Request a new one below.",
                "too_many_attempts" => "Too many attempts. Request a new code below.",
                "no_pending_verification" => "Nothing to verify for this email. Try logging in or signing up.",
                _ => "Verification failed. Please try again."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error verifying email. Email={Email}", Email);
            ErrorMessage = "An error occurred. Please try again.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Missing email. Please sign up again.";
            return Page();
        }

        try
        {
            var code = await _authClient.ResendVerificationAsync(Email);
            try
            {
                await _emailQueue.EnqueueAsync(EmailTemplates.VerificationEmail(Email, code));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue resend verification email. Email={Email}", Email);
            }
            StatusMessage = $"A new code was sent to {Email}.";
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "resend_cooldown" => "Please wait a minute before requesting another code.",
                "no_pending_verification" => "Nothing to verify for this email. Try logging in or signing up.",
                _ => "Could not resend the code. Please try again."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resending verification. Email={Email}", Email);
            ErrorMessage = "An error occurred. Please try again.";
        }

        return Page();
    }
}
