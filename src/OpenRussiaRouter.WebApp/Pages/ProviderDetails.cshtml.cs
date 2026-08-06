using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Currency;
using OpenRussiaRouter.WebApp.Services.Markup;

namespace OpenRussiaRouter.WebApp.Pages;

public class ProviderDetailsModel(
    BillingProvider billingProvider,
    CurrencyFormatter currencyFormatter,
    AuthProvider authProvider,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ProviderDetailsModel> logger) : CatalogPageModelBase
{
    protected override AuthProvider AuthProvider => authProvider;
    protected override CurrencyFormatter CurrencyFormatter => currencyFormatter;
    protected override ILogger Logger => logger;
    protected override string LogContext => "provider details";
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = "";

    public ProviderInfo? Provider { get; private set; }
    public IReadOnlyList<ProviderModelRow> Models { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public string? Description => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
        ? Provider?.DescriptionRu
        : Provider?.DescriptionEn;

    public async Task OnGetAsync()
    {
        Currency = await ResolveCurrencyAsync();
        try
        {
            Provider = await billingProvider.GetModelProviderAsync(Id, HttpContext.RequestAborted);
            if (Provider is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                ErrorMessage = localizer["ProviderDetails_NotFound"];
                return;
            }

            var allModelsTask = billingProvider.ListPublicModelsAsync(HttpContext.RequestAborted);
            var tagsTask = billingProvider.ListTagsAsync(HttpContext.RequestAborted);

            await Task.WhenAll(allModelsTask, tagsTask);

            var allModels = await allModelsTask;
            var tagsById = (await tagsTask).ToDictionary(t => t.Id, StringComparer.Ordinal);

            Models = allModels
                .Where(m => string.Equals(m.ProviderId, Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.Priority)
                .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Select(m =>
                {
                    var tags = (m.TagIds ?? [])
                        .Select(tagId => tagsById.TryGetValue(tagId, out var tag)
                            ? new ProviderTagRow(tag.Id, tag.DisplayName, tag.Color)
                            : new ProviderTagRow(tagId, tagId, null))
                        .ToList();

                    return new ProviderModelRow(
                        m.Id,
                        m.DisplayName,
                        m.InputPrice,
                        m.OutputPrice,
                        m.CachePrice,
                        m.ModelType,
                        m.ContextLength is > 0 ? CatalogFormatting.FormatContext(m.ContextLength.Value) : "—",
                        tags);
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load provider details. ProviderId={ProviderId}", Id);
            ErrorMessage = localizer["Providers_LoadFailed"];
        }
    }

    public static string GetInitials(string name) => CatalogFormatting.GetInitials(name);

    public sealed record ProviderModelRow(
        string Id,
        string DisplayName,
        decimal InputPrice,
        decimal OutputPrice,
        decimal CacheWritePrice,
        string ModelType,
        string Context,
        IReadOnlyList<ProviderTagRow> Tags);

    public sealed record ProviderTagRow(string Id, string DisplayName, string? Color);
}
