using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages;

public class ResetPasswordModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly IEmailDispatchQueue _emailQueue;
    private readonly ILogger<ResetPasswordModel> _logger;

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code.")]
    public string Code { get; set; } = "";

    [BindProperty]
    [Required, MinLength(12, ErrorMessage = "Password must be at least 12 characters.")]
    public string NewPassword { get; set; } = "";

    [BindProperty]
    [Required, Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    public ResetPasswordModel(AuthProvider authProvider, IEmailDispatchQueue emailQueue, ILogger<ResetPasswordModel> logger)
    {
        _authClient = authProvider;
        _emailQueue = emailQueue;
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
            ErrorMessage = "Missing email. Please start over.";
            return Page();
        }
        if (!ModelState.IsValid) return Page();

        try
        {
            await _authClient.ResetPasswordAsync(Email, Code, NewPassword);

            TempData["StatusMessage"] = "Your password has been reset. Log in with the new password.";
            return RedirectToPage("/Login");
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "invalid_code" => "Incorrect code. Please check and try again.",
                "code_expired" => "That code has expired. Request a new one below.",
                "too_many_attempts" => "Too many attempts. Request a new code below.",
                "no_pending_reset" => "No pending password reset for this email. Request a new code below.",
                "weak_password" => "Password must be at least 12 characters.",
                _ => "Password reset failed. Please try again."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resetting password. Email={Email}", Email);
            ErrorMessage = "An error occurred. Please try again.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Missing email. Please start over.";
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
            StatusMessage = $"If an account exists for {Email}, a new code was sent.";
        }
        catch (AuthException ex) when (ex.ErrorCode == "resend_cooldown")
        {
            ErrorMessage = "Please wait a minute before requesting another code.";
        }
        catch (AuthException)
        {
            // Same generic message as success — do not disclose account existence.
            StatusMessage = $"If an account exists for {Email}, a new code was sent.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resending password reset code. Email={Email}", Email);
            ErrorMessage = "An error occurred. Please try again.";
        }

        return Page();
    }
}
