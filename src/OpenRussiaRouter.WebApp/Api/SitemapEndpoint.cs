using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.WebApp.Services;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Catalog;
using OpenRussiaRouter.WebApp.Services.Content;

namespace OpenRussiaRouter.WebApp.Api;

/// <summary>
/// <c>GET /sitemap.xml</c> — dynamic XML sitemap for search engines.
/// Emits the static indexable pages plus dynamic catalog URLs: enabled models
/// (<c>/models/{provider}/{slug}</c>), active providers (<c>/providers/{id}</c>) and published
/// tools (<c>/tools/{id}</c>). The absolute base URL comes from <see cref="ApiBaseUrlResolver"/>
/// so the sitemap follows the admin-configured public origin instead of whatever host served the
/// request. Private/service URLs (workspace, admin, payments, auth) are never included.
/// </summary>
public static class SitemapEndpoint
{
    public const string Route = "/sitemap.xml";

    /// <summary>Static indexable pages per the SEO index/noindex strategy.</summary>
    private static readonly string[] StaticPaths =
    [
        "/",
        "/models",
        "/providers",
        "/tools",
        "/docs",
        "/support",
        "/legal/offer",
        "/legal/privacy",
    ];

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(Route, HandleAsync);

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        ApiBaseUrlResolver baseUrlResolver,
        BillingProvider billing,
        ContentProvider content,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("SitemapEndpoint");
        try
        {
            var baseUrl = (await baseUrlResolver.ResolveAsync(request, ct)).TrimEnd('/');

            var modelsTask = billing.ListPublicModelsAsync(ct);
            var providersTask = billing.ListModelProvidersAsync(ct);
            var toolsTask = content.ListToolsAsync(cancellationToken: ct);
            await Task.WhenAll(modelsTask, providersTask, toolsTask);

            var entries = Collect(baseUrl, await modelsTask, await providersTask, await toolsTask);
            return Results.Content(BuildXml(entries), "application/xml", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate {Route}.", Route);
            // Never hand out a silently truncated sitemap: a search engine keeps the last good copy.
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Collects the sitemap entries: static pages, then dynamic catalog URLs.
    /// Only enabled models (the <c>ListPublicModels</c> source already filters them), providers that
    /// have at least one enabled model, and published tools are included. <paramref name="baseUrl"/>
    /// is used as the origin; paths are percent-encoded per the sitemap protocol.
    /// Model URLs come from <see cref="ModelDetailRoute"/> — the sitemap must not invent its own shape,
    /// or it would list URLs the detail page redirects away from (or 404s on).
    /// </summary>
    internal static IReadOnlyList<SitemapEntry> Collect(
        string baseUrl,
        IReadOnlyList<ModelInfo> models,
        IReadOnlyList<ProviderInfo> providers,
        IReadOnlyList<ToolSummaryInfo> tools)
    {
        var origin = baseUrl.TrimEnd('/');
        var entries = new List<SitemapEntry>();

        foreach (var path in StaticPaths)
        {
            entries.Add(new SitemapEntry(origin + path));
        }

        // "Active" provider = one an enabled model points at via ProviderId. Providers without
        // models would render thin empty pages (bad SEO) and are excluded.
        var activeProviderIds = models
            .Where(m => !string.IsNullOrWhiteSpace(m.ProviderId))
            .Select(m => m.ProviderId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(provider.Id) || !activeProviderIds.Contains(provider.Id))
            {
                continue;
            }

            entries.Add(new SitemapEntry(
                origin + $"/providers/{Uri.EscapeDataString(provider.Id)}",
                NormalizeLastMod(provider.UpdatedAtUtc)));
        }

        foreach (var model in models.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            var path = ModelDetailRoute.TryBuildPath(model.Id, model.ProviderId, model.Provider);
            if (path is not null)
            {
                entries.Add(new SitemapEntry(origin + path));
            }
        }

        foreach (var tool in tools
                     .Where(t => t.IsPublished && !string.IsNullOrWhiteSpace(t.Id))
                     .OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            entries.Add(new SitemapEntry(
                origin + $"/tools/{Uri.EscapeDataString(tool.Id)}",
                NormalizeLastMod(tool.UpdatedAtUtc)));
        }

        return entries;
    }

    /// <summary>
    /// Serializes entries to the sitemaps.org XML document. <c>lastmod</c> is emitted only when a
    /// real update date exists, as a W3C date (<c>yyyy-MM-dd</c>).
    /// </summary>
    internal static string BuildXml(IReadOnlyList<SitemapEntry> entries)
    {
        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var urlset = new XElement(ns + "urlset");

        foreach (var entry in entries)
        {
            var url = new XElement(ns + "url", new XElement(ns + "loc", entry.Url));
            if (entry.LastModUtc is { } lastMod)
            {
                url.Add(new XElement(ns + "lastmod",
                    lastMod.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }

            urlset.Add(url);
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), urlset);
        // XDocument.ToString() strips the declaration; prepend it so the response carries the encoding.
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + document.ToString();
    }

    private static DateTimeOffset? NormalizeLastMod(DateTimeOffset value) =>
        value == default ? null : value;

    internal sealed record SitemapEntry(string Url, DateTimeOffset? LastModUtc = null);
}
