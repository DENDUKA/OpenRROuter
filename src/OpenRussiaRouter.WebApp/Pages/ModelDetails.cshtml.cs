using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp;
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
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<ModelDetailsModel> _logger;

    public ModelDetailsModel(
        BillingProvider billingProvider,
        CurrencyFormatter currencyFormatter,
        AuthProvider authProvider,
        ApiBaseUrlResolver baseUrlResolver,
        IStringLocalizer<SharedResource> localizer,
        ILogger<ModelDetailsModel> logger)
    {
        _billingClient = billingProvider;
        _currencyFormatter = currencyFormatter;
        _authProvider = authProvider;
        _baseUrlResolver = baseUrlResolver;
        _localizer = localizer;
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
        : _localizer["Model_ContextUnknown"];
    public string ModalitiesDisplay => ModelData?.Modalities ?? "Text";
    public IReadOnlyList<string> InputModalities => ParseModalities(ModelData?.InputModalities ?? ModelData?.Modalities);
    public IReadOnlyList<string> OutputModalities => ParseModalities(ModelData?.OutputModalities ?? "text");
    public IReadOnlyList<string> AllModalities { get; } = ["text", "image", "audio", "video"];
    public decimal InputPrice => ModelData?.InputPrice ?? 0;
    public decimal OutputPrice => ModelData?.OutputPrice ?? 0;
    public decimal CachePrice => ModelData?.CachePrice ?? 0;
    public bool HasPricing => InputPrice > 0 || OutputPrice > 0;
    public bool IsAudioModel => string.Equals(ModelData?.ModelType, "audio", StringComparison.OrdinalIgnoreCase);

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
            ModelData = await _billingClient.GetModelAsync(ModelId)
                      ?? await _billingClient.GetModelAsync(Slug);
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

    private static IReadOnlyList<string> ParseModalities(string? modalities) =>
        string.IsNullOrWhiteSpace(modalities)
            ? []
            : modalities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public static string ModalityIcon(string modality) => modality.ToLowerInvariant() switch
    {
        "text" => "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z\"/><polyline points=\"14 2 14 8 20 8\"/><line x1=\"16\" y1=\"13\" x2=\"8\" y2=\"13\"/><line x1=\"16\" y1=\"17\" x2=\"8\" y2=\"17\"/></svg>",
        "image" => "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\" ry=\"2\"/><circle cx=\"8.5\" cy=\"8.5\" r=\"1.5\"/><polyline points=\"21 15 16 10 5 21\"/></svg>",
        "audio" => "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M9 18V5l12-2v13\"/><circle cx=\"6\" cy=\"18\" r=\"3\"/><circle cx=\"18\" cy=\"16\" r=\"3\"/></svg>",
        "video" => "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><polygon points=\"23 7 16 12 23 17 23 7\"/><rect x=\"1\" y=\"5\" width=\"15\" height=\"14\" rx=\"2\" ry=\"2\"/></svg>",
        _ => $"<span style=\"font-size:12px\">{modality}</span>"
    };
}
