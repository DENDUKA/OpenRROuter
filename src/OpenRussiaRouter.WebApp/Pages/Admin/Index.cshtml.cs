using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class IndexModel(BillingProvider billingProvider, ApiBaseUrlResolver apiBaseUrlResolver, ILogger<IndexModel> logger) : PageModelBase
{
    [BindProperty]
    public string PublicApiBaseUrl { get; set; } = "";

    [BindProperty]
    public bool StartBalanceEnabled { get; set; }

    [BindProperty]
    public decimal StartBalanceAmountUsd { get; set; } = 1.00m;

    [BindProperty]
    public bool FirstTopUpEnabled { get; set; }

    [BindProperty]
    public decimal FirstTopUpPercent { get; set; } = 20.00m;

    public string? SaveMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            PublicApiBaseUrl = await billingProvider.GetPlatformSettingAsync(
                PlatformSettingKeys.PublicApiBaseUrl, HttpContext.RequestAborted) ?? "";
            StartBalanceEnabled = await GetBoolSettingAsync(PlatformSettingKeys.PromotionsStartBalanceEnabled);
            StartBalanceAmountUsd = await GetDecimalSettingAsync(PlatformSettingKeys.PromotionsStartBalanceAmountUsd, 1.00m);
            FirstTopUpEnabled = await GetBoolSettingAsync(PlatformSettingKeys.PromotionsFirstTopUpEnabled);
            FirstTopUpPercent = await GetDecimalSettingAsync(PlatformSettingKeys.PromotionsFirstTopUpPercent, 20.00m);
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
            apiBaseUrlResolver.InvalidateCache();
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

    public async Task<IActionResult> OnPostSavePromoAsync()
    {
        try
        {
            if (StartBalanceAmountUsd <= 0 || StartBalanceAmountUsd > 100)
            {
                ErrorMessage = "Start balance amount must be between $0.01 and $100.";
                await ReloadPromoSettingsAsync();
                return Page();
            }
            if (FirstTopUpPercent <= 0 || FirstTopUpPercent > 100)
            {
                ErrorMessage = "First top-up bonus percent must be between 0.01% and 100%.";
                await ReloadPromoSettingsAsync();
                return Page();
            }

            await billingProvider.SetPlatformSettingAsync(
                PlatformSettingKeys.PromotionsStartBalanceEnabled, StartBalanceEnabled.ToString(), HttpContext.RequestAborted);
            await billingProvider.SetPlatformSettingAsync(
                PlatformSettingKeys.PromotionsStartBalanceAmountUsd, StartBalanceAmountUsd.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), HttpContext.RequestAborted);
            await billingProvider.SetPlatformSettingAsync(
                PlatformSettingKeys.PromotionsFirstTopUpEnabled, FirstTopUpEnabled.ToString(), HttpContext.RequestAborted);
            await billingProvider.SetPlatformSettingAsync(
                PlatformSettingKeys.PromotionsFirstTopUpPercent, FirstTopUpPercent.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), HttpContext.RequestAborted);
            SaveMessage = "Promotion settings saved.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save promo settings.");
            ErrorMessage = "Failed to save promotion settings.";
        }

        return Page();
    }

    private async Task ReloadPromoSettingsAsync()
    {
        try
        {
            StartBalanceEnabled = await GetBoolSettingAsync(PlatformSettingKeys.PromotionsStartBalanceEnabled);
            StartBalanceAmountUsd = await GetDecimalSettingAsync(PlatformSettingKeys.PromotionsStartBalanceAmountUsd, 1.00m);
            FirstTopUpEnabled = await GetBoolSettingAsync(PlatformSettingKeys.PromotionsFirstTopUpEnabled);
            FirstTopUpPercent = await GetDecimalSettingAsync(PlatformSettingKeys.PromotionsFirstTopUpPercent, 20.00m);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to reload promo settings.");
        }
    }

    private async Task<bool> GetBoolSettingAsync(string key)
    {
        var raw = await billingProvider.GetPlatformSettingAsync(key, HttpContext.RequestAborted);
        return bool.TryParse(raw, out var v) && v;
    }

    private async Task<decimal> GetDecimalSettingAsync(string key, decimal defaultValue)
    {
        var raw = await billingProvider.GetPlatformSettingAsync(key, HttpContext.RequestAborted);
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    }
}
