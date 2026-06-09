using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp.Services;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Currency;

namespace OpenRussiaRouter.WebApp.Pages;

public class ModelDetailsModel : PageModelBase
{
    private readonly BillingProvider _billingClient;
    private readonly CurrencyFormatter _currencyFormatter;
    private readonly AuthProvider _authProvider;
    private readonly ApiBaseUrlResolver _baseUrlResolver;
    private readonly ILogger<ModelDetailsModel> _logger;

    public ModelDetailsModel(
        BillingProvider billingProvider,
        CurrencyFormatter currencyFormatter,
        AuthProvider authProvider,
        ApiBaseUrlResolver baseUrlResolver,
        ILogger<ModelDetailsModel> logger)
    {
        _billingClient = billingProvider;
        _currencyFormatter = currencyFormatter;
        _authProvider = authProvider;
        _baseUrlResolver = baseUrlResolver;
        _logger = logger;
    }

    /// <summary>Display currency: profile choice for signed-in users, RUB for anonymous.</summary>
    public DisplayCurrency Currency { get; private set; } = DisplayCurrency.Rub;

    public string FormatPrice(decimal usdPrice) => _currencyFormatter.Format(usdPrice, Currency);

    [BindProperty(SupportsGet = true)]
    public string Provider { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Slug { get; set; } = "";

    public string ModelId => $"{Provider}/{Slug}";

    public ModelInfo? ModelData { get; private set; }

    public string DisplayName => ModelData?.DisplayName ?? ToTitle(Slug.Replace('-', ' '));
    public string ProviderTitle => ToTitle(Provider);
    public string Initials => string.Concat(
        ProviderTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Take(2)
                     .Select(p => char.ToUpperInvariant(p[0])));

    public string? Description => ModelData?.Description;
    public string ContextDisplay => ModelData?.ContextLength is > 0
        ? FormatContext(ModelData.ContextLength.Value)
        : "Unknown";
    public string ModalitiesDisplay => ModelData?.Modalities ?? "Text";
    public decimal InputPrice => ModelData?.InputPrice ?? 0;
    public decimal OutputPrice => ModelData?.OutputPrice ?? 0;
    public decimal CachePrice => ModelData?.CachePrice ?? 0;
    public bool HasPricing => InputPrice > 0 || OutputPrice > 0;

    /// <summary>The public catalog id (preferred) for copy-paste snippets, falling back to the route id.</summary>
    public string PublicModelId => ModelData?.Id ?? ModelId;

    public string ApiBaseUrl { get; private set; } = "";

    public IntegrationExamplesModel Integration => new(
        $"{ApiBaseUrl}/v1",
        PublicModelId,
        ModelData?.ContextLength is > 0 ? ModelData.ContextLength.Value : 128000);

    public async Task OnGetAsync()
    {
        Currency = await ResolveCurrencyAsync();
        ApiBaseUrl = await _baseUrlResolver.ResolveAsync(Request, HttpContext.RequestAborted);
        try
        {
            var models = await _billingClient.ListModelsAsync();

            ModelData = models.FirstOrDefault(m =>
                string.Equals(m.Id, ModelId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(m.UpstreamModelId, ModelId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
        }
    }

    private async Task<DisplayCurrency> ResolveCurrencyAsync()
    {
        var userId = CurrentUser?.UserId;
        if (string.IsNullOrEmpty(userId)) return DisplayCurrency.Rub;
        try
        {
            var user = await _authProvider.GetUserAsync(userId, HttpContext.RequestAborted);
            return DisplayCurrencyExtensions.Parse(user?.DisplayCurrency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load currency for model details. UserId={UserId}", userId);
            return DisplayCurrency.Rub;
        }
    }

    private static string FormatContext(int tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000M:0.#}M"
        : tokens >= 1_000 ? $"{tokens / 1_000}K"
        : tokens.ToString();

    private static string ToTitle(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
}
