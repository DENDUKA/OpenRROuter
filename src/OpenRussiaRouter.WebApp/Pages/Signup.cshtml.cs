using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages;

public class SignupModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly IEmailDispatchQueue _emailQueue;
    private readonly ILogger<SignupModel> _logger;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public SignupModel(AuthProvider authProvider, IEmailDispatchQueue emailQueue, ILogger<SignupModel> logger)
    {
        _authClient = authProvider;
        _emailQueue = emailQueue;
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Workspace/ApiKeys");

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        if (!Input.AcceptLegal)
        {
            ModelState.AddModelError("Input.AcceptLegal", "You must accept the Public Offer and Privacy Policy.");
            return Page();
        }

        try
        {
            var result = await _authClient.RegisterAsync(Input.Email, Input.Password);

            // Email the code; a failure here must not abandon the signup — the user can
            // request a resend from the verify page.
            try
            {
                await _emailQueue.EnqueueAsync(EmailTemplates.VerificationEmail(result.Email, result.VerificationCode));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue verification email. Email={Email}", result.Email);
            }

            TempData["StatusMessage"] = $"We sent a 6-digit code to {result.Email}. Enter it below to finish signing up.";
            return RedirectToPage("/VerifyEmail", new { email = result.Email });
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "email_already_registered" => "An account with this email already exists. Try logging in instead.",
                "weak_password" => "Password must be at least 12 characters.",
                _ => "Sign up failed. Please try again."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during signup. Email={Email}", Input.Email);
            ErrorMessage = "An error occurred. Please try again.";
        }

        return Page();
    }

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, MinLength(12, ErrorMessage = "Password must be at least 12 characters.")]
        public string Password { get; set; } = "";

        [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";

        [Required(ErrorMessage = "You must accept the Public Offer and Privacy Policy.")]
        public bool AcceptLegal { get; set; }
    }
}
