using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Controllers;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class SettingsModel(AuthProvider authProvider, IStringLocalizer<SharedResource> localizer, ILogger<SettingsModel> logger) : PageModelBase
{
    private readonly IStringLocalizer<SharedResource> _localizer = localizer;
    [BindProperty]
    public string SelectedCurrency { get; set; } = "RUB";

    [BindProperty]
    public PasswordInputModel PasswordInput { get; set; } = new();

    public string? SaveMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PasswordMessage { get; set; }
    public string? PasswordErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            var user = await authProvider.GetUserAsync(User.GetUserId());
            if (user != null) SelectedCurrency = user.DisplayCurrency;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load user profile. UserId={UserId}", User.GetUserId());
        }
    }

    public async Task<IActionResult> OnPostSaveCurrencyAsync()
    {
        if (SelectedCurrency is not ("RUB" or "USD" or "EUR"))
        {
            ErrorMessage = _localizer["Settings_InvalidCurrency"];
            return Page();
        }

        try
        {
            var userId = User.GetUserId();
            var user = await authProvider.GetUserAsync(userId);
            if (user == null)
            {
                ErrorMessage = _localizer["Settings_UserNotFound"];
                return Page();
            }

            await authProvider.UpdateUserAsync(
                userId,
                displayName: user.DisplayName,
                maxApiKeys: user.MaxApiKeys,
                displayCurrency: SelectedCurrency,
                cancellationToken: HttpContext.RequestAborted);

            PreferencesController.SetCurrencyCookie(Response, SelectedCurrency);
            SaveMessage = _localizer["Settings_Saved"];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to save settings. UserId={UserId}", User.GetUserId());
            ErrorMessage = _localizer["Settings_SaveFailed"];
        }

        return Page();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        await OnGetAsync();

        // The currency form is not posted by this handler; only validate the password fields.
        ModelState.Clear();
        if (!TryValidateModel(PasswordInput, nameof(PasswordInput)))
        {
            return Page();
        }

        try
        {
            await authProvider.ChangePasswordAsync(
                User.GetUserId(),
                PasswordInput.CurrentPassword,
                PasswordInput.NewPassword,
                HttpContext.RequestAborted);

            PasswordInput = new PasswordInputModel();
            ModelState.Clear();
            PasswordMessage = _localizer["Password_Changed"];
        }
        catch (AuthException ex)
        {
            PasswordErrorMessage = ex.ErrorCode switch
            {
                "invalid_credentials" => _localizer["Password_CurrentIncorrect"],
                "weak_password" => _localizer["Auth_WeakPassword"],
                "user_disabled" => _localizer["Auth_UserDisabled"],
                _ => _localizer["Password_ChangeFailed"]
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to change password. UserId={UserId}", User.GetUserId());
            PasswordErrorMessage = _localizer["Password_ChangeFailed"];
        }

        return Page();
    }

    public class PasswordInputModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        public string CurrentPassword { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [MinLength(12, ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_PasswordMin12")]
        public string NewPassword { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [Compare(nameof(NewPassword), ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_PasswordsMismatch")]
        public string ConfirmNewPassword { get; set; } = "";
    }
}
