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
using OpenRussiaRouter.WebApp.Services.Localization;

namespace OpenRussiaRouter.WebApp.Api;

/// <summary>
/// <c>GET /sitemap.xml</c> — dynamic XML sitemap for search engines.
/// Emits the static indexable pages plus dynamic catalog URLs: enabled models
/// (<c>/models/{provider}/{slug}</c>), active providers (<c>/providers/{id}</c>) and published
/// tools (<c>/tools/{id}</c>). The absolute base URL comes from <see cref="SiteBaseUrlResolver"/>
/// (the canonical site origin) — never from the admin-configured API base, so the sitemap only ever
/// lists URLs on the crawler-facing domain. Private/service URLs (workspace, admin, payments, auth)
/// are never included.
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
        SiteBaseUrlResolver baseUrlResolver,
        BillingProvider billing,
        ContentProvider content,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("SitemapEndpoint");
        try
        {
            var baseUrl = baseUrlResolver.Resolve(request);

            var modelsTask = billing.ListPublicModelsAsync(ct);
            var providersTask = billing.ListModelProvidersAsync(ct);
            var toolsTask = content.ListToolsAsync(cancellationToken: ct);
            await Task.WhenAll(modelsTask, providersTask, toolsTask);

            var entries = Collect(await modelsTask, await providersTask, await toolsTask);
            return Results.Content(BuildXml(baseUrl, entries), "application/xml", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate {Route}.", Route);
            // Never hand out a silently truncated sitemap: a search engine keeps the last good copy.
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Collects the sitemap entries as <b>locale-free paths</b>: static pages, then dynamic catalog
    /// URLs. Only enabled models (the <c>ListPublicModels</c> source already filters them), providers
    /// that have at least one enabled model, and published tools are included. Paths are
    /// percent-encoded per the sitemap protocol; <see cref="BuildXml"/> adds the origin and fans each
    /// entry out across the addressable locales.
    /// Model URLs come from <see cref="ModelDetailRoute"/> — the sitemap must not invent its own shape,
    /// or it would list URLs the detail page redirects away from (or 404s on).
    /// </summary>
    internal static IReadOnlyList<SitemapEntry> Collect(
        IReadOnlyList<ModelInfo> models,
        IReadOnlyList<ProviderInfo> providers,
        IReadOnlyList<ToolSummaryInfo> tools)
    {
        var entries = new List<SitemapEntry>();

        foreach (var path in StaticPaths)
        {
            // /docs is served by the VitePress container, which lists its own bilingual tree in
            // /docs/sitemap.xml and annotates it itself. Fanning it out here would claim /en/docs,
            // a URL this app answers only with a redirect.
            entries.Add(new SitemapEntry(path, Localized: !LocaleUrl.IsDocsPath(path)));
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
                $"/providers/{Uri.EscapeDataString(provider.Id)}",
                NormalizeLastMod(provider.UpdatedAtUtc)));
        }

        foreach (var model in models.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            var path = ModelDetailRoute.TryBuildPath(model.Id, model.ProviderId, model.Provider);
            if (path is not null)
            {
                entries.Add(new SitemapEntry(path));
            }
        }

        foreach (var tool in tools
                     .Where(t => t.IsPublished && !string.IsNullOrWhiteSpace(t.Id))
                     .OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            entries.Add(new SitemapEntry(
                $"/tools/{Uri.EscapeDataString(tool.Id)}",
                NormalizeLastMod(tool.UpdatedAtUtc)));
        }

        return entries;
    }

    /// <summary>
    /// Serializes entries to the sitemaps.org XML document, one <c>&lt;url&gt;</c> per locale.
    /// <c>lastmod</c> is emitted only when a real update date exists, as a W3C date
    /// (<c>yyyy-MM-dd</c>).
    /// </summary>
    /// <remarks>
    /// Every localized entry carries the full <c>xhtml:link</c> alternate set — including a link to
    /// itself — plus <c>x-default</c>. That is the form search engines require: an alternate set that
    /// is not reciprocal and self-referencing on every member is discarded wholesale. The annotations
    /// must stay byte-identical to the <c>hreflang</c> tags in <c>_Layout</c>; both are generated
    /// from <see cref="LocaleUrl"/> so they cannot drift apart.
    /// </remarks>
    internal static string BuildXml(string baseUrl, IReadOnlyList<SitemapEntry> entries)
    {
        var origin = baseUrl.TrimEnd('/');
        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var xhtml = XNamespace.Get("http://www.w3.org/1999/xhtml");
        var urlset = new XElement(ns + "urlset", new XAttribute(XNamespace.Xmlns + "xhtml", xhtml));

        foreach (var entry in entries)
        {
            if (!entry.Localized)
            {
                urlset.Add(BuildUrl(ns, origin + entry.Path, entry.LastModUtc, alternates: null));
                continue;
            }

            var alternates = LocaleUrl.Alternates(origin, entry.Path).ToList();
            var xDefault = alternates.First(a => a.Culture == LocaleUrl.XDefaultCulture).Url;

            foreach (var (_, url) in alternates)
            {
                var annotations = alternates
                    .Select(a => new XElement(xhtml + "link",
                        new XAttribute("rel", "alternate"),
                        new XAttribute("hreflang", a.Culture),
                        new XAttribute("href", a.Url)))
                    .Append(new XElement(xhtml + "link",
                        new XAttribute("rel", "alternate"),
                        new XAttribute("hreflang", "x-default"),
                        new XAttribute("href", xDefault)));

                urlset.Add(BuildUrl(ns, url, entry.LastModUtc, annotations));
            }
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), urlset);
        // XDocument.ToString() strips the declaration; prepend it so the response carries the encoding.
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + document.ToString();
    }

    private static XElement BuildUrl(
        XNamespace ns,
        string loc,
        DateTimeOffset? lastMod,
        IEnumerable<XElement>? alternates)
    {
        var url = new XElement(ns + "url", new XElement(ns + "loc", loc));
        if (lastMod is { } value)
        {
            url.Add(new XElement(ns + "lastmod",
                value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (alternates is not null)
        {
            url.Add(alternates);
        }

        return url;
    }

    private static DateTimeOffset? NormalizeLastMod(DateTimeOffset value) =>
        value == default ? null : value;

    /// <summary>
    /// One indexable page, as a locale-free path. <paramref name="Localized"/> is false for pages
    /// that exist at exactly one URL regardless of language.
    /// </summary>
    internal sealed record SitemapEntry(string Path, DateTimeOffset? LastModUtc = null, bool Localized = true);
}
