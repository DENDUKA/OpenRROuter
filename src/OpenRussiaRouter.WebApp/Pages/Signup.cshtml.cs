using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using AspNet.Security.OAuth.Yandex;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Analytics;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages;

public class SignupModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly BillingProvider _billingProvider;
    private readonly IEmailDispatchQueue _emailQueue;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<SignupModel> _logger;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public bool StartBalanceEnabled { get; set; }
    public decimal StartBalanceAmountUsd { get; set; }
    public bool GoogleEnabled { get; }
    public bool YandexEnabled { get; }

    public SignupModel(AuthProvider authProvider, BillingProvider billingProvider, IEmailDispatchQueue emailQueue, IStringLocalizer<SharedResource> localizer, ILogger<SignupModel> logger, IConfiguration configuration)
    {
        _authClient = authProvider;
        _billingProvider = billingProvider;
        _emailQueue = emailQueue;
        _localizer = localizer;
        _logger = logger;
        GoogleEnabled = !string.IsNullOrWhiteSpace(configuration["Google:ClientId"])
                     && !string.IsNullOrWhiteSpace(configuration["Google:ClientSecret"]);
        YandexEnabled = !string.IsNullOrWhiteSpace(configuration["Yandex:ClientId"])
                     && !string.IsNullOrWhiteSpace(configuration["Yandex:ClientSecret"]);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Workspace/ApiKeys");

        try
        {
            (StartBalanceEnabled, StartBalanceAmountUsd, _, _) =
                await _billingProvider.GetPromotionSettingsAsync(HttpContext.RequestAborted);
        }
        catch
        {
            // Promo display is best-effort
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        if (!Input.AcceptLegal)
        {
            ModelState.AddModelError("Input.AcceptLegal", _localizer["Validation_AcceptLegal"]);
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

            TempData["StatusMessage"] = _localizer["Auth_VerificationCodeSent", result.Email].Value;
            AnalyticsTempData.Enqueue(TempData, AnalyticsGoal.Named(AnalyticsGoals.Signup));
            return RedirectToPage("/VerifyEmail", new { email = result.Email });
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "email_already_registered" => _localizer["Auth_EmailAlreadyRegistered"],
                "weak_password" => _localizer["Auth_WeakPassword"],
                _ => _localizer["Auth_SignupFailed"]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during signup. Email={Email}", Input.Email);
            ErrorMessage = _localizer["Auth_SignupError"];
        }

        return Page();
    }

    public IActionResult OnPostGoogle()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Workspace/ApiKeys");

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
            return RedirectToPage("/Workspace/ApiKeys");

        var props = new AuthenticationProperties
        {
            RedirectUri = "/Login/ExternalCallback",
            Items = { ["provider"] = "yandex" }
        };
        return Challenge(props, YandexAuthenticationDefaults.AuthenticationScheme);
    }

    public class InputModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [EmailAddress(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [MinLength(12, ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_PasswordMin12")]
        public string Password { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [Compare(nameof(Password), ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_PasswordsMismatch")]
        public string ConfirmPassword { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_AcceptLegal")]
        public bool AcceptLegal { get; set; }
    }
}
