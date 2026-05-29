using Microsoft.Extensions.Caching.Memory;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages;

public class ModelsModel(
    BillingProvider BillingProvider,
    ControlPlaneProvider ControlPlaneProvider,
    IMemoryCache memoryCache,
    ILogger<ModelsModel> logger) : PageModelBase
{
    private const string CatalogCacheKey = "public-models-node-catalog";

    public IReadOnlyList<PublicModelRow> Models { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public bool CatalogMetadataUnavailable { get; private set; }

    public static readonly string[] Categories = ["All", "OpenAI", "Anthropic", "Google", "Meta", "Text", "Vision", "Coding"];

    public async Task OnGetAsync()
    {
        try
        {
            var billingModels = await BillingProvider.ListPublicModelsAsync(HttpContext.RequestAborted);
            var catalog = await GetCachedCatalogAsync(HttpContext.RequestAborted);
            CatalogMetadataUnavailable = !string.IsNullOrWhiteSpace(catalog.Error);

            var catalogByUpstream = catalog.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.UpstreamId))
                .GroupBy(e => e.UpstreamId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            Models = billingModels
                .Select(model =>
                {
                    catalogByUpstream.TryGetValue(model.UpstreamModelId, out var catalogEntry);
                    return MapModel(model, catalogEntry);
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load public model catalog.");
            ErrorMessage = "Failed to load models.";
        }
    }

    public static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "AI";
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    public static string FormatPrice(decimal price) =>
        $"{price:0.##} $ / 1M";

    public static string FormatContext(int? tokens) =>
        tokens is > 0
            ? tokens >= 1_000_000 ? $"{tokens / 1_000_000M:0.#}M"
              : tokens >= 1_000 ? $"{tokens / 1_000}K"
              : tokens.ToString()!
            : "—";

    private async Task<NodeModelCatalogResult> GetCachedCatalogAsync(CancellationToken cancellationToken)
    {
        return await memoryCache.GetOrCreateAsync(CatalogCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await ControlPlaneProvider.ListNodeModelCatalogAsync(cancellationToken);
        }) ?? new NodeModelCatalogResult([], [], [], "Cached catalog is empty.");
    }

    private static PublicModelRow MapModel(ModelInfo model, NodeModelCatalogEntry? catalogEntry)
    {
        var sourceProviders = catalogEntry?.SourceProviders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var provider = sourceProviders.Count == 0
            ? "Unknown"
            : string.Join(", ", sourceProviders);

        var capability = !string.IsNullOrWhiteSpace(catalogEntry?.ModelType)
            ? ToTitle(catalogEntry.ModelType)
            : !string.IsNullOrWhiteSpace(model.Modalities)
                ? ToTitle(model.Modalities)
                : "Unknown";

        return new PublicModelRow(
            model.Id,
            model.DisplayName,
            model.UpstreamModelId,
            model.InputPrice,
            model.OutputPrice,
            provider,
            capability,
            FormatContext(model.ContextLength));
    }

    private static string ToTitle(string value)
    {
        var parts = value.Replace('-', ' ').Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length == 0
            ? "Unknown"
            : string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public sealed record PublicModelRow(
        string Id,
        string DisplayName,
        string UpstreamModelId,
        decimal InputPrice,
        decimal OutputPrice,
        string Provider,
        string Capability,
        string Context);
}
