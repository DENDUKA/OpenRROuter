using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages;

public class ProviderDetailsModel(
    BillingProvider billingProvider,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ProviderDetailsModel> logger) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = "";

    public ProviderInfo? Provider { get; private set; }
    public IReadOnlyList<ModelRow> Models { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public string? Description => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
        ? Provider?.DescriptionRu
        : Provider?.DescriptionEn;

    public async Task OnGetAsync()
    {
        try
        {
            Provider = await billingProvider.GetModelProviderAsync(Id, HttpContext.RequestAborted);
            if (Provider is null)
            {
                ErrorMessage = localizer["ProviderDetails_NotFound"];
                return;
            }

            var allModels = await billingProvider.ListPublicModelsAsync(HttpContext.RequestAborted);
            Models = allModels
                .Where(m => string.Equals(m.ProviderId, Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new ModelRow(
                    m.Id,
                    m.DisplayName,
                    m.ContextLength is > 0 ? FormatContext(m.ContextLength.Value) : "—",
                    m.ModelType))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load provider details. ProviderId={ProviderId}", Id);
            ErrorMessage = localizer["Providers_LoadFailed"];
        }
    }

    private static string FormatContext(int tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000M:0.#}M"
        : tokens >= 1_000 ? $"{tokens / 1_000}K"
        : tokens.ToString();

    public sealed record ModelRow(string Id, string DisplayName, string Context, string ModelType);
}
