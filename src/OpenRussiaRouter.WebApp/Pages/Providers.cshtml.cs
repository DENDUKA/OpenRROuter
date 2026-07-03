using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages;

public class ProvidersModel(
    BillingProvider billingProvider,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ProvidersModel> logger) : PageModelBase
{
    public IReadOnlyList<ProviderDisplayInfo> Providers { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var providers = await billingProvider.ListModelProvidersAsync(HttpContext.RequestAborted);
            Providers = providers
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(p => new ProviderDisplayInfo(
                    p.Id,
                    p.DisplayName,
                    GetCurrentDescription(p),
                    $"/providers/logo/{Uri.EscapeDataString(p.Id)}?v={p.UpdatedAtUtc.UtcDateTime.Ticks}"))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load providers list.");
            ErrorMessage = localizer["Providers_LoadFailed"];
        }
    }

    private string? GetCurrentDescription(ProviderInfo provider)
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return culture == "ru" ? provider.DescriptionRu : provider.DescriptionEn;
    }

    public sealed record ProviderDisplayInfo(
        string Id,
        string DisplayName,
        string? Description,
        string LogoUrl);
}
