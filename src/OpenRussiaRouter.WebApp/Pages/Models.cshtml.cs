using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Collector;
using OpenRussiaRouter.WebApp.Services.ControlPlane; // NodeModelCatalogResult, NodeModelCatalogEntry
using OpenRussiaRouter.WebApp.Services.Currency;
using OpenRussiaRouter.WebApp.Services.Markup;

namespace OpenRussiaRouter.WebApp.Pages;

public class ModelsModel(
    BillingProvider BillingProvider,
    CollectorProvider CollectorProvider,
    CurrencyFormatter currencyFormatter,
    AuthProvider authProvider,
    IMemoryCache memoryCache,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ModelsModel> logger) : CatalogPageModelBase
{
    protected override AuthProvider AuthProvider => authProvider;
    protected override CurrencyFormatter CurrencyFormatter => currencyFormatter;
    protected override ILogger Logger => logger;
    protected override string LogContext => "catalog";

    private const string CatalogCacheKey = "public-models-node-catalog";
    private const string ProvidersCacheKey = "public-models-providers";
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ProvidersCacheDuration = TimeSpan.FromHours(1);
    private static readonly SemaphoreSlim ProvidersCacheLock = new(1, 1);

    // NOTE: do not store `localizer` in a field — the primary-constructor parameter is
    // captured by the lambda below, and capturing it in a field as well trips CS9124
    // (TreatWarningsAsErrors=true). Pass it explicitly where needed instead.

    public IReadOnlyList<PublicModelRow> Models { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public bool CatalogMetadataUnavailable { get; private set; }

    public IReadOnlyList<FilterCategory> ProviderCategories { get; private set; } = [];
    public IReadOnlyList<FilterCategory> CapabilityCategories { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Currency = await ResolveCurrencyAsync();
        try
        {
            var billingModelsTask = BillingProvider.ListPublicModelsAsync(HttpContext.RequestAborted);
            var tagsTask = BillingProvider.ListTagsAsync(HttpContext.RequestAborted);
            var catalogTask = GetCachedCatalogAsync(HttpContext.RequestAborted);

            await Task.WhenAll(billingModelsTask, tagsTask, catalogTask);

            var billingModels = await billingModelsTask;
            var catalog = await catalogTask;
            CatalogMetadataUnavailable = !string.IsNullOrWhiteSpace(catalog.Error);
            var tagsById = (await tagsTask).ToDictionary(t => t.Id, StringComparer.Ordinal);

            var providers = await GetCachedProvidersAsync(HttpContext.RequestAborted);

            ProviderCategories = providers
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(p => new FilterCategory(
                    p.Id,
                    p.DisplayName,
                    $"/providers/logo/{p.Id}"))
                .ToList();

            CapabilityCategories =
            [
                new("Text", localizer["Catalog_Category_Text"], null),
                new("Vision", localizer["Catalog_Category_Vision"], null),
                new("Coding", localizer["Catalog_Category_Coding"], null),
            ];

            var catalogByUpstream = catalog.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.UpstreamId))
                .GroupBy(e => e.UpstreamId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            Models = billingModels
                .OrderByDescending(model => model.IsFlagship)
                .ThenByDescending(model => model.Priority, Comparer<int>.Default)
                .ThenBy(model => model.Id, StringComparer.Ordinal)
                .Select(model =>
                {
                    catalogByUpstream.TryGetValue(model.UpstreamModelId, out var catalogEntry);
                    return MapModel(model, catalogEntry, tagsById, localizer);
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load public model catalog.");
            ErrorMessage = localizer["Models_LoadFailed"];
        }
    }

    public static string GetInitials(string name) => CatalogFormatting.GetInitials(name);

    private static readonly Dictionary<string, string> KnownProviderIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = "/icons/openai.png",
        ["anthropic"] = "/icons/claude.png",
        ["google"] = "/icons/gemini.png",
        ["meta"] = "/icons/llama.png",
    };

    public static bool TryGetStaticIcon(string provider, out string iconPath)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            iconPath = null!;
            return false;
        }
        foreach (var (key, path) in KnownProviderIcons)
        {
            if (provider.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                iconPath = path;
                return true;
            }
        }
        iconPath = null!;
        return false;
    }

    private async Task<NodeModelCatalogResult> GetCachedCatalogAsync(CancellationToken cancellationToken)
    {
        return await memoryCache.GetOrCreateAsync(CatalogCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CatalogCacheDuration;
            return await CollectorProvider.ListNodeModelCatalogAsync(cancellationToken);
        }) ?? new NodeModelCatalogResult([], [], [], "Cached catalog is empty.");
    }

    private async Task<IReadOnlyList<CachedProviderInfo>> GetCachedProvidersAsync(CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(ProvidersCacheKey, out IReadOnlyList<CachedProviderInfo>? cached))
        {
            return cached ?? [];
        }

        await ProvidersCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (memoryCache.TryGetValue(ProvidersCacheKey, out cached))
            {
                return cached ?? [];
            }

            var providers = await BillingProvider.ListModelProvidersAsync(cancellationToken);
            var projection = providers
                .Where(p => !string.IsNullOrEmpty(p.Id))
                .Select(p => new CachedProviderInfo(p.Id, p.DisplayName, p.LogoBytes is { Length: > 0 }))
                .ToList();

            memoryCache.Set(ProvidersCacheKey, projection, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ProvidersCacheDuration,
            });

            return projection;
        }
        finally
        {
            ProvidersCacheLock.Release();
        }
    }

    private static PublicModelRow MapModel(
        ModelInfo model,
        NodeModelCatalogEntry? catalogEntry,
        IReadOnlyDictionary<string, TagInfo> tagsById,
        IStringLocalizer<SharedResource> localizer)
    {
        var sourceProviders = catalogEntry?.SourceProviders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

            var provider = !string.IsNullOrWhiteSpace(model.Provider)
                ? model.Provider
                : (sourceProviders.Count == 0 ? localizer["Model_ContextUnknown"] : string.Join(", ", sourceProviders));

        var capability = !string.IsNullOrWhiteSpace(catalogEntry?.ModelType)
            ? ToTitle(catalogEntry.ModelType)
            : !string.IsNullOrWhiteSpace(model.Modalities)
                ? ToTitle(model.Modalities)
                : localizer["Model_ContextUnknown"];

        var tags = (model.TagIds ?? [])
            .Select(tagId => tagsById.TryGetValue(tagId, out var tag)
                ? new PublicTagRow(tag.Id, tag.DisplayName, tag.Color)
                : new PublicTagRow(tagId, tagId, null))
            .ToList();

        return new PublicModelRow(
            model.Id,
            model.DisplayName,
            model.UpstreamModelId,
            model.InputPrice,
            model.OutputPrice,
            model.CachePrice,
            model.ModelType,
            provider,
            model.ProviderId,
            capability,
                CatalogFormatting.FormatContext(model.ContextLength),
            tags);
    }

    private static string ToTitle(string value)
    {
        var parts = value.Replace('-', ' ').Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public sealed record PublicModelRow(
        string Id,
        string DisplayName,
        string UpstreamModelId,
        decimal InputPrice,
        decimal OutputPrice,
        decimal CacheWritePrice,
        string ModelType,
        string Provider,
        string? ProviderId,
        string Capability,
        string Context,
        IReadOnlyList<PublicTagRow> Tags);

    public sealed record PublicTagRow(
        string Id,
        string DisplayName,
        string? Color);

    public sealed record FilterCategory(string Id, string DisplayName, string? IconUrl);

    /// <summary>
    /// Lightweight projection of <see cref="ProviderInfo"/> for memory-cache.
    /// Strips <c>LogoBytes</c> (~256 KiB each) to keep the cached footprint small.
    /// </summary>
    public sealed record CachedProviderInfo(string Id, string DisplayName, bool HasLogo);
}
