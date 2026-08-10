using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using OpenRussiaRouter.WebApp.Services.Localization;

namespace OpenRussiaRouter.WebApp.TagHelpers;

/// <summary>
/// Keeps root-relative <c>&lt;a href&gt;</c> and <c>&lt;form action&gt;</c> targets inside the
/// locale the current request was reached through.
/// </summary>
/// <remarks>
/// <para>
/// The views link with literal paths (<c>href="/models"</c>) rather than <c>asp-page</c>, and a
/// literal path is the one kind of URL ASP.NET Core does <b>not</b> rewrite with
/// <see cref="HttpRequest.PathBase"/>. Without this tag helper the first click on <c>/en/models</c>
/// would drop the visitor back onto the Russian tree — and a crawler following that link would
/// conclude the English pages are orphans.
/// </para>
/// <para>
/// It keys off <c>PathBase</c>, not off the current culture: links stay in the contour the visitor
/// is already browsing. A signed-in user whose cookie says English but who is on a bare URL keeps
/// bare URLs, which is what leaves the private areas (workspace, admin) cookie-driven and free of
/// indexable locale variants.
/// </para>
/// </remarks>
[HtmlTargetElement("a", Attributes = HrefAttribute)]
[HtmlTargetElement("form", Attributes = ActionAttribute)]
public sealed class LocalePathTagHelper : TagHelper
{
    private const string HrefAttribute = "href";
    private const string ActionAttribute = "action";

    /// <summary>
    /// Paths that must keep their bare form in every locale: machine endpoints with no UI, and the
    /// crawler-facing files that are defined to live at the origin root.
    /// </summary>
    private static readonly string[] NeverLocalized =
    [
        "/webhook", "/internal", "/health", "/metrics", "/.well-known",
        "/sitemap.xml", "/robots.txt", "/og-image.png", "/favicon.png",
    ];

    /// <summary>Runs after the framework's anchor/form tag helpers so it sees the final URL.</summary>
    public override int Order => int.MaxValue;

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var request = ViewContext?.HttpContext?.Request;
        var pathBase = request?.PathBase.Value;
        if (string.IsNullOrEmpty(pathBase))
        {
            return;
        }

        var attributeName = output.TagName == "form" ? ActionAttribute : HrefAttribute;
        if (!output.Attributes.TryGetAttribute(attributeName, out var attribute))
        {
            return;
        }

        var url = attribute.Value as string ?? attribute.Value?.ToString();
        var localized = Localize(url, pathBase);
        if (localized is null)
        {
            return;
        }

        output.Attributes.SetAttribute(new TagHelperAttribute(
            attribute.Name, localized, attribute.ValueStyle));
    }

    /// <summary>
    /// The locale-scoped form of <paramref name="url"/>, or <c>null</c> when it must be left alone.
    /// Internal so the rewriting rules can be tested without standing up a view.
    /// </summary>
    internal static string? Localize(string? url, string pathBase)
    {
        // Only root-relative paths are ours. "//host" is protocol-relative (i.e. external),
        // "#", "?", "mailto:" and absolute URLs all address something this prefix cannot reach.
        if (string.IsNullOrEmpty(url) || url[0] != '/' || url.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        // Split the query/fragment off before matching: "/models?q=x" is still the models page.
        var stop = url.AsSpan().IndexOfAny('?', '#');
        var path = stop < 0 ? url : url[..stop];
        var tail = stop < 0 ? string.Empty : url[stop..];

        if (LocaleUrl.IsDocsPath(path))
        {
            // The docs container nests its English tree inside its own base, not under /en.
            var docs = LocaleUrl.DocsFor(path, LocaleUrl.Split(pathBase).Culture);
            return string.Equals(docs, path, StringComparison.Ordinal) ? null : docs + tail;
        }

        if (path.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase)
            && (path.Length == pathBase.Length || path[pathBase.Length] == '/'))
        {
            // Already inside the locale contour — a tilde-resolved URL, or a link written prefixed.
            return null;
        }

        foreach (var reserved in NeverLocalized)
        {
            if (path.Equals(reserved, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(reserved + "/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        // "/en/" would 301 to "/en" — never link to a URL that redirects.
        return path == "/" ? pathBase + tail : pathBase + url;
    }
}
