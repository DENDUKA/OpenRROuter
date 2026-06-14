using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages;

public class ResetPasswordModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly IEmailDispatchQueue _emailQueue;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<ResetPasswordModel> _logger;

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
    [RegularExpression(@"^\d{6}$", ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Code6Digits")]
    public string Code { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
    [MinLength(12, ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_PasswordMin12")]
    public string NewPassword { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
    [Compare(nameof(NewPassword), ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_PasswordsMismatch")]
    public string ConfirmPassword { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    public ResetPasswordModel(AuthProvider authProvider, IEmailDispatchQueue emailQueue, IStringLocalizer<SharedResource> localizer, ILogger<ResetPasswordModel> logger)
    {
        _authClient = authProvider;
        _emailQueue = emailQueue;
        _localizer = localizer;
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        if (string.IsNullOrWhiteSpace(Email))
            return RedirectToPage("/ForgotPassword");

        StatusMessage = TempData["StatusMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = _localizer["Auth_MissingEmail"];
            return Page();
        }
        if (!ModelState.IsValid) return Page();

        try
        {
            await _authClient.ResetPasswordAsync(Email, Code, NewPassword);

            TempData["StatusMessage"] = _localizer["Auth_PasswordResetOk"];
            return RedirectToPage("/Login");
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "invalid_code" => _localizer["Auth_InvalidCode"],
                "code_expired" => _localizer["Auth_CodeExpired"],
                "too_many_attempts" => _localizer["Auth_TooManyAttempts"],
                "no_pending_reset" => _localizer["Auth_NoPendingReset"],
                "weak_password" => _localizer["Auth_WeakPassword"],
                _ => _localizer["Auth_ResetFailed"]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resetting password. Email={Email}", Email);
            ErrorMessage = _localizer["Auth_LoginError"];
        }

        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = _localizer["Auth_MissingEmail"];
            return Page();
        }

        // Clear the validation state of the main form — this handler only resends the code.
        ModelState.Clear();

        try
        {
            var (email, code) = await _authClient.RequestPasswordResetAsync(Email);
            try
            {
                await _emailQueue.EnqueueAsync(EmailTemplates.PasswordResetEmail(email, code));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue resend password reset email. Email={Email}", email);
            }
            StatusMessage = _localizer["Auth_PasswordResetResent", Email];
        }
        catch (AuthException ex) when (ex.ErrorCode == "resend_cooldown")
        {
            ErrorMessage = _localizer["Auth_ResendCooldown"];
        }
        catch (AuthException)
        {
            // Same generic message as success — do not disclose account existence.
            StatusMessage = _localizer["Auth_PasswordResetResent", Email];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resending password reset code. Email={Email}", Email);
            ErrorMessage = _localizer["Auth_LoginError"];
        }

        return Page();
    }
}
