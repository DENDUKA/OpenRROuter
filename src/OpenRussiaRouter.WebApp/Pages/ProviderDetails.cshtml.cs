using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Currency;

namespace OpenRussiaRouter.WebApp.Pages;

public class ProviderDetailsModel(
    BillingProvider billingProvider,
    CurrencyFormatter currencyFormatter,
    AuthProvider authProvider,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ProviderDetailsModel> logger) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = "";

    public ProviderInfo? Provider { get; private set; }
    public IReadOnlyList<ProviderModelRow> Models { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public string? Description => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
        ? Provider?.DescriptionRu
        : Provider?.DescriptionEn;

    public DisplayCurrency Currency { get; private set; } = DisplayCurrency.Usd;
    public string CurrencySymbol => Currency.Symbol();

    public async Task OnGetAsync()
    {
        Currency = await ResolveCurrencyAsync();
        try
        {
            Provider = await billingProvider.GetModelProviderAsync(Id, HttpContext.RequestAborted);
            if (Provider is null)
            {
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
                        m.ContextLength is > 0 ? FormatContext(m.ContextLength.Value) : "—",
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

    public static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "AI";
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    public string FormatPrice(decimal usdPrice) =>
        currencyFormatter.Format(usdPrice, Currency);

    private async Task<DisplayCurrency> ResolveCurrencyAsync()
    {
        var userId = CurrentUser?.UserId;
        if (string.IsNullOrEmpty(userId)) return DisplayCurrency.Usd;
        try
        {
            var user = await authProvider.GetUserAsync(userId, HttpContext.RequestAborted);
            return DisplayCurrencyExtensions.Parse(user?.DisplayCurrency);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load currency for provider details. UserId={UserId}", userId);
            return DisplayCurrency.Usd;
        }
    }

    private static string FormatContext(int tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000M:0.#}M"
        : tokens >= 1_000 ? $"{tokens / 1_000}K"
        : tokens.ToString();

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
