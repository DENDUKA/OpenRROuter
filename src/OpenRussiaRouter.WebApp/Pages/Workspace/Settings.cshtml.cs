using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Controllers;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class SettingsModel(AuthProvider authProvider, ILogger<SettingsModel> logger) : PageModelBase
{
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
            ErrorMessage = "Invalid currency selection.";
            return Page();
        }

        try
        {
            var userId = User.GetUserId();
            var user = await authProvider.GetUserAsync(userId);
            if (user == null)
            {
                ErrorMessage = "User not found.";
                return Page();
            }

            await authProvider.UpdateUserAsync(
                userId,
                displayName: user.DisplayName,
                maxApiKeys: user.MaxApiKeys,
                displayCurrency: SelectedCurrency,
                cancellationToken: HttpContext.RequestAborted);

            PreferencesController.SetCurrencyCookie(Response, SelectedCurrency);
            SaveMessage = "Settings saved.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to save settings. UserId={UserId}", User.GetUserId());
            ErrorMessage = "Failed to save settings.";
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
            PasswordMessage = "Password changed.";
        }
        catch (AuthException ex)
        {
            PasswordErrorMessage = ex.ErrorCode switch
            {
                "invalid_credentials" => "Current password is incorrect.",
                "weak_password" => "New password must be at least 12 characters.",
                "user_disabled" => "Your account has been disabled.",
                _ => "Failed to change the password. Please try again."
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to change password. UserId={UserId}", User.GetUserId());
            PasswordErrorMessage = "Failed to change the password. Please try again.";
        }

        return Page();
    }

    public class PasswordInputModel
    {
        [Required]
        public string CurrentPassword { get; set; } = "";

        [Required, MinLength(12, ErrorMessage = "Password must be at least 12 characters.")]
        public string NewPassword { get; set; } = "";

        [Required, Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        public string ConfirmNewPassword { get; set; } = "";
    }
}
