using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class IndexModel(BillingProvider billingProvider, ILogger<IndexModel> logger) : PageModelBase
{
    [BindProperty]
    public string PublicApiBaseUrl { get; set; } = "";

    public string? SaveMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            PublicApiBaseUrl = await billingProvider.GetPlatformSettingAsync(
                PlatformSettingKeys.PublicApiBaseUrl, HttpContext.RequestAborted) ?? "";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load platform settings.");
        }
    }

    public async Task<IActionResult> OnPostSaveSettingsAsync()
    {
        var value = (PublicApiBaseUrl ?? "").Trim().TrimEnd('/');
        try
        {
            await billingProvider.SetPlatformSettingAsync(
                PlatformSettingKeys.PublicApiBaseUrl, value, HttpContext.RequestAborted);
            PublicApiBaseUrl = value;
            SaveMessage = "Settings saved.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save platform settings.");
            ErrorMessage = "Failed to save settings.";
        }

        return Page();
    }
}
