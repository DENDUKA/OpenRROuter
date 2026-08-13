using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Catalog;
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

    public ModelInfo? ModelData { get; private set; }

    public string DisplayName => ModelData?.DisplayName ?? ToTitle(Slug.Replace('-', ' '));

    public string ProviderTitle => !string.IsNullOrWhiteSpace(ModelData?.Provider)
        ? ModelData.Provider
        : ToTitle(Provider);

    /// <summary>Provider segment for the breadcrumb link to <c>/providers/{id}</c>.</summary>
    public string ProviderRouteId => !string.IsNullOrWhiteSpace(ModelData?.ProviderId)
        ? ModelData.ProviderId
        : Provider;

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

    /// <summary>The public catalog id (preferred) for copy-paste snippets, falling back to the route slug.</summary>
    public string PublicModelId => ModelData?.Id ?? Slug;

    public string ApiBaseUrl { get; private set; } = "";

    private const int RelatedModelsLimit = 8;

    /// <summary>Other models of the same provider, already resolved to canonical detail paths.</summary>
    public IReadOnlyList<RelatedModel> RelatedModels { get; private set; } = [];

    public sealed record RelatedModel(string DisplayName, string Id, string Path);

    public IntegrationExamplesModel Integration => new(
        $"{ApiBaseUrl}/v1",
        PublicModelId,
        ModelData?.ContextLength is > 0 ? ModelData.ContextLength.Value : 128000);

    public async Task<IActionResult> OnGetAsync()
    {
        Currency = await ResolveCurrencyAsync();
        ApiBaseUrl = await _baseUrlResolver.ResolveAsync(Request, HttpContext.RequestAborted);

        ModelData = await ResolveModelAsync(HttpContext.RequestAborted);
        if (ModelData is null)
        {
            return NotFound();
        }

        // One model = one URL. Legacy links (provider display name in the segment, the whole id in the
        // slug, the id prefix as the provider) resolve to the same model above and are moved onto the
        // canonical path instead of serving duplicate content under a second URL.
        if (!ModelDetailRoute.IsCanonical(ModelData.Id, ModelData.ProviderId, ModelData.Provider, Provider, Slug)
            && ModelDetailRoute.TryBuildPath(ModelData.Id, ModelData.ProviderId, ModelData.Provider) is { } canonical)
        {
            return RedirectPermanent(canonical);
        }

        RelatedModels = await LoadRelatedModelsAsync(ModelData, HttpContext.RequestAborted);

        return Page();
    }

    /// <summary>
    /// Sibling models of the same provider, for the "other models" block. This is the page's only
    /// outbound link into the rest of the catalog: without it a model page is a dead end for both
    /// readers and crawlers. A catalog failure is not fatal — the block simply disappears.
    /// </summary>
    private async Task<IReadOnlyList<RelatedModel>> LoadRelatedModelsAsync(ModelInfo current, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(current.ProviderId))
        {
            return [];
        }

        try
        {
            var catalog = await _billingClient.ListPublicModelsAsync(ct);
            return catalog
                .Where(m => string.Equals(m.ProviderId, current.ProviderId, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(m.Id, current.Id, StringComparison.Ordinal))
                .OrderByDescending(m => m.Priority)
                .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Select(m => new
                {
                    Model = m,
                    Path = ModelDetailRoute.TryBuildPath(m.Id, m.ProviderId, m.Provider),
                })
                .Where(x => x.Path is not null)
                .Take(RelatedModelsLimit)
                .Select(x => new RelatedModel(x.Model.DisplayName, x.Model.Id, x.Path!))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load related models for {ModelId}", current.Id);
            return [];
        }
    }

    /// <summary>
    /// Resolves the model addressed by <c>{provider}/{slug}</c>. The public id is not
    /// <c>"{provider}/{slug}"</c> (see <see cref="ModelDetailRoute"/>), so the slug is looked up
    /// directly first and only prefixed or legacy-addressed ids need the catalog scan.
    /// </summary>
    private async Task<ModelInfo?> ResolveModelAsync(CancellationToken ct)
    {
        var slug = ModelDetailRoute.NormalizeSlug(Slug);
        if (slug.Length == 0)
        {
            return null;
        }

        try
        {
            // Fast path: most public ids are exactly the slug ("claude-opus-4-8").
            var direct = await _billingClient.GetModelAsync(slug, ct);
            if (direct is not null)
            {
                return direct;
            }

            // Prefixed ids ("RWT/Gemini-3.1-Pro") are not addressable by the slug alone — only the
            // public catalog maps provider + slug back onto the full id.
            var catalog = await _billingClient.ListPublicModelsAsync(ct);
            return catalog.FirstOrDefault(m =>
                       ModelDetailRoute.Matches(m.Id, m.ProviderId, m.Provider, Provider, slug))
                   // Wrong provider segment (old link, hand-typed URL): the slug alone identifies the
                   // model and the canonical redirect above sends the request to the right provider.
                   ?? catalog.FirstOrDefault(m =>
                       string.Equals(ModelDetailRoute.SlugOf(m.Id), slug, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load model {Provider}/{Slug}", Provider, slug);
            return null;
        }
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
