using Microsoft.Extensions.Caching.Memory;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Collector;
using OpenRussiaRouter.WebApp.Services.ControlPlane; // NodeModelCatalogResult, NodeModelCatalogEntry

namespace OpenRussiaRouter.WebApp.Pages;

public class ModelsModel(
    BillingProvider BillingProvider,
    CollectorProvider CollectorProvider,
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
            var billingModelsTask = BillingProvider.ListPublicModelsAsync(HttpContext.RequestAborted);
            var tagsTask = BillingProvider.ListTagsAsync(HttpContext.RequestAborted);
            var catalogTask = GetCachedCatalogAsync(HttpContext.RequestAborted);

            await Task.WhenAll(billingModelsTask, tagsTask, catalogTask);

            var billingModels = await billingModelsTask;
            var catalog = await catalogTask;
            CatalogMetadataUnavailable = !string.IsNullOrWhiteSpace(catalog.Error);
            var tagsById = (await tagsTask).ToDictionary(t => t.Id, StringComparer.Ordinal);

            var catalogByUpstream = catalog.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.UpstreamId))
                .GroupBy(e => e.UpstreamId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            Models = billingModels
                .Select(model =>
                {
                    catalogByUpstream.TryGetValue(model.UpstreamModelId, out var catalogEntry);
                    return MapModel(model, catalogEntry, tagsById);
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
        $"{price:0.##} $";

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
            return await CollectorProvider.ListNodeModelCatalogAsync(cancellationToken);
        }) ?? new NodeModelCatalogResult([], [], [], "Cached catalog is empty.");
    }

    private static PublicModelRow MapModel(
        ModelInfo model,
        NodeModelCatalogEntry? catalogEntry,
        IReadOnlyDictionary<string, TagInfo> tagsById)
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
            provider,
            capability,
            FormatContext(model.ContextLength),
            tags);
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
        decimal CacheWritePrice,
        string Provider,
        string Capability,
        string Context,
        IReadOnlyList<PublicTagRow> Tags);

    public sealed record PublicTagRow(
        string Id,
        string DisplayName,
        string? Color);
}
