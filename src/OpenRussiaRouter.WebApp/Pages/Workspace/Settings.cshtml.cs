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

    public string? SaveMessage { get; set; }
    public string? ErrorMessage { get; set; }

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
}
