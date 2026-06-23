using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages;

public class ForgotPasswordModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly IEmailDispatchQueue _emailQueue;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<ForgotPasswordModel> _logger;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public ForgotPasswordModel(AuthProvider authProvider, IEmailDispatchQueue emailQueue, IStringLocalizer<SharedResource> localizer, ILogger<ForgotPasswordModel> logger)
    {
        _authClient = authProvider;
        _emailQueue = emailQueue;
        _localizer = localizer;
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Workspace/Settings");

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            var (email, code) = await _authClient.RequestPasswordResetAsync(Input.Email);
            try
            {
                await _emailQueue.EnqueueAsync(EmailTemplates.PasswordResetEmail(email, code));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue password reset email. Email={Email}", email);
            }
        }
        catch (AuthException ex) when (ex.ErrorCode == "resend_cooldown")
        {
            ErrorMessage = _localizer["Auth_ResendCooldown"];
            return Page();
        }
        catch (AuthException)
        {
            // password_reset_not_available: unknown email, inactive account or
            // external-login-only account. Fall through to the same confirmation page
            // so account existence is not disclosed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error requesting password reset. Email={Email}", Input.Email);
            ErrorMessage = _localizer["Auth_LoginError"];
            return Page();
        }

        TempData["StatusMessage"] = _localizer["Auth_PasswordResetSent", Input.Email].Value;
        return RedirectToPage("/ResetPassword", new { email = Input.Email });
    }

    public class InputModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [EmailAddress(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Email")]
        public string Email { get; set; } = "";
    }
}
