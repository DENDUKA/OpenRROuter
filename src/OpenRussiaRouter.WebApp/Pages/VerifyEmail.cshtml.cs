using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Analytics;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Pages;

public class VerifyEmailModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly BillingProvider _billingProvider;
    private readonly IEmailDispatchQueue _emailQueue;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<VerifyEmailModel> _logger;
    private readonly ISupportBotNotifier? _botNotifier;

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
    [RegularExpression(@"^\d{6}$", ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Code6Digits")]
    public string Code { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    public VerifyEmailModel(
        AuthProvider authProvider,
        BillingProvider billingProvider,
        IEmailDispatchQueue emailQueue,
        IStringLocalizer<SharedResource> localizer,
        ILogger<VerifyEmailModel> logger,
        ISupportBotNotifier? botNotifier = null)
    {
        _authClient = authProvider;
        _billingProvider = billingProvider;
        _emailQueue = emailQueue;
        _localizer = localizer;
        _logger = logger;
        _botNotifier = botNotifier;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Workspace/ApiKeys");

        StatusMessage = TempData["StatusMessage"] as string;
        TrackGoals(AnalyticsTempData.Dequeue(TempData));
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
            var response = await _authClient.VerifyEmailAsync(Email, Code);

            _botNotifier?.NotifyUserRegistered(response.Email);

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

            // Grant start balance if promotion is enabled (best-effort)
            try
            {
                var enabledRaw = await _billingProvider.GetPlatformSettingAsync(
                    PlatformSettingKeys.PromotionsStartBalanceEnabled, HttpContext.RequestAborted);
                if (bool.TryParse(enabledRaw, out var enabled) && enabled)
                {
                    var amountUsdRaw = await _billingProvider.GetPlatformSettingAsync(
                        PlatformSettingKeys.PromotionsStartBalanceAmountUsd, HttpContext.RequestAborted);
                    if (decimal.TryParse(amountUsdRaw, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var amountUsd) && amountUsd > 0)
                    {
                        await _billingProvider.GrantStartBalanceAsync(response.UserId, amountUsd, HttpContext.RequestAborted);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to grant start balance after email verification. UserId={UserId}", response.UserId);
            }

            AnalyticsTempData.Enqueue(TempData, AnalyticsGoal.Named(AnalyticsGoals.EmailVerified));
            return RedirectToPage("/Workspace/ApiKeys");
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "invalid_code" => _localizer["Auth_InvalidCode"],
                "code_expired" => _localizer["Auth_CodeExpired"],
                "too_many_attempts" => _localizer["Auth_TooManyAttempts"],
                "no_pending_verification" => _localizer["Auth_NoPendingVerification"],
                _ => _localizer["Auth_VerificationFailed"]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error verifying email. Email={Email}", Email);
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
            StatusMessage = _localizer["Auth_VerificationResent", Email];
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "resend_cooldown" => _localizer["Auth_ResendCooldown"],
                "no_pending_verification" => _localizer["Auth_NoPendingVerification"],
                _ => _localizer["Auth_ResendFailed"]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resending verification. Email={Email}", Email);
            ErrorMessage = _localizer["Auth_LoginError"];
        }

        return Page();
    }
}
