using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Currency;
using OpenRussiaRouter.WebApp.Services.Markup;

namespace OpenRussiaRouter.WebApp.Pages;

public class ModelDetailsModel : CatalogPageModelBase
{
    private readonly BillingProvider _billingClient;
    private readonly ApiBaseUrlResolver _baseUrlResolver;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<ModelDetailsModel> _logger;

    protected override AuthProvider AuthProvider { get; }
    protected override CurrencyFormatter CurrencyFormatter { get; }
    protected override ILogger Logger => _logger;
    protected override string LogContext => "model details";

    public ModelDetailsModel(
        BillingProvider billingProvider,
        CurrencyFormatter currencyFormatter,
        AuthProvider authProvider,
        ApiBaseUrlResolver baseUrlResolver,
        IStringLocalizer<SharedResource> localizer,
        ILogger<ModelDetailsModel> logger)
    {
        _billingClient = billingProvider;
        CurrencyFormatter = currencyFormatter;
        AuthProvider = authProvider;
        _baseUrlResolver = baseUrlResolver;
        _localizer = localizer;
        _logger = logger;
    }

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

    public string? DescriptionRu => ModelData?.DescriptionRu;
    public string? DescriptionEn => ModelData?.DescriptionEn;

    /// <summary>
    /// Markdown rendered to sanitized HTML for the current UI culture, with fallback to the other
    /// language if the preferred one is empty.
    /// </summary>
    public string DescriptionHtml
    {
        get
        {
            var preferred = IsRussianCulture() ? ModelData?.DescriptionRu : ModelData?.DescriptionEn;
            var fallback = IsRussianCulture() ? ModelData?.DescriptionEn : ModelData?.DescriptionRu;
            var markdown = !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;
            return MarkdownRenderer.Render(markdown);
        }
    }

    private static bool IsRussianCulture() =>
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase);

    public string ContextDisplay => ModelData?.ContextLength is > 0
        ? CatalogFormatting.FormatContext(ModelData.ContextLength.Value)
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

    public async Task<IActionResult> OnGetAsync()
    {
        Currency = await ResolveCurrencyAsync();
        ApiBaseUrl = await _baseUrlResolver.ResolveAsync(Request, HttpContext.RequestAborted);
        try
        {
            ModelData = await _billingClient.GetModelAsync(ModelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load model {ModelId}", ModelId);
        }

        return ModelData is null ? NotFound() : Page();
    }

    private static string ToTitle(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    private static IReadOnlyList<string> ParseModalities(string? modalities) =>
        string.IsNullOrWhiteSpace(modalities)
            ? []
            : modalities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public static string ModalityIcon(string modality) => ModalityIcons.GetIcon(modality, 16);
}
