namespace OpenRussiaRouter.WebApp.Services.Localization;

/// <summary>
/// The single source of truth for the locale-prefixed URL contract.
/// </summary>
/// <remarks>
/// <para>
/// Russian lives under a <c>/ru</c> prefix (<c>/ru/models</c>) and English under a <c>/en</c> prefix
/// (<c>/en/models</c>). Both locales have explicit, addressable URL prefixes.
/// </para>
/// <para>
/// The prefix is stripped into <see cref="Microsoft.AspNetCore.Http.HttpRequest.PathBase"/> by
/// <see cref="LocalePrefixMiddleware"/>, so page routes, link generation and
/// <c>ModelDetailRoute</c> all keep working on bare paths and know nothing about locales.
/// Anything that emits an <b>absolute</b> URL for a crawler (canonical, hreflang, sitemap) must go
/// through this class instead of concatenating paths by hand.
/// </para>
/// </remarks>
public static class LocaleUrl
{
    /// <summary>The prefix segment of the Russian locale, without a trailing slash.</summary>
    public const string RussianPrefix = "/ru";

    /// <summary>The prefix segment of the English locale, without a trailing slash.</summary>
    public const string EnglishPrefix = "/en";

    /// <summary>
    /// Locales that appear in the public URL contract, in the order search engines should see them.
    /// Kept separate from <see cref="SupportedCultures.All"/>: a culture can be renderable in the UI
    /// long before it has an addressable, fully translated URL tree worth pointing hreflang at.
    /// </summary>
    public static readonly string[] AddressableCultures = ["ru", "en"];

    /// <summary>
    /// The locale an <c>hreflang="x-default"</c> annotation points at. English, not Russian: the
    /// tag targets visitors whose language matches none of our versions — a developer in Tehran,
    /// Istanbul or Almaty is served far better by English than by Russian.
    /// </summary>
    public const string XDefaultCulture = "en";

    /// <summary>
    /// The path prefix carried by <paramref name="culture"/>.
    /// </summary>
    public static string PrefixFor(string? culture) =>
        string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase) ? EnglishPrefix :
        string.Equals(culture, "ru", StringComparison.OrdinalIgnoreCase) ? RussianPrefix :
        RussianPrefix;

    /// <summary>
    /// Splits a raw incoming path into its locale and the bare path underneath it.
    /// <paramref name="path"/> is expected to be a rooted request path (<c>/en/models</c> or <c>/ru/models</c>).
    /// A bare <c>/en</c> or <c>/ru</c> maps to <c>/</c> so the home page is not a special case downstream.
    /// </summary>
    public static (string Culture, string Path) Split(string? path)
    {
        var value = string.IsNullOrEmpty(path) ? "/" : path;

        foreach (var culture in AddressableCultures)
        {
            var prefix = "/" + culture;
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Only a full segment counts: "/entrypoint" must not be read as "/en" + "trypoint".
            var rest = value[prefix.Length..];
            if (rest.Length == 0)
            {
                return (culture, "/");
            }

            if (rest[0] == '/')
            {
                return (culture, rest);
            }
        }

        return (SupportedCultures.DefaultCulture, value);
    }

    /// <summary>
    /// The public path of <paramref name="path"/> in <paramref name="culture"/>.
    /// <paramref name="path"/> must be the bare, locale-free path.
    /// </summary>
    public static string For(string? path, string? culture)
    {
        var bare = string.IsNullOrEmpty(path) ? "/" : path;
        var prefix = PrefixFor(culture);

        // "/en" + "/" would render as "/en/", which the canonical normalizer then redirects away
        // from — emit the same "/en" (or "/ru") the middleware and the sitemap agree on.
        return bare == "/" ? prefix : prefix + bare;
    }

    /// <summary>
    /// Absolute per-locale URLs for a bare path, in <see cref="AddressableCultures"/> order.
    /// Used for hreflang annotations and sitemap alternates, which must agree exactly.
    /// </summary>
    public static IEnumerable<(string Culture, string Url)> Alternates(string baseUrl, string? path)
    {
        var origin = baseUrl.TrimEnd('/');
        foreach (var culture in AddressableCultures)
        {
            yield return (culture, origin + For(path, culture));
        }
    }

    /// <summary>The path prefix owned by the VitePress documentation container.</summary>
    public const string DocsPath = "/docs";

    /// <summary>
    /// Prefixes of documentation sections in the VitePress documentation tree that crawlers
    /// (e.g. Googlebot) may discover from client-side JSON without the <c>/docs</c> base prefix.
    /// </summary>
    public static readonly string[] DocsSectionPrefixes = ["/guide", "/api", "/integrations"];

    /// <summary>
    /// Whether <paramref name="path"/> matches one of the known documentation section prefixes
    /// (<c>/guide</c>, <c>/api</c>, <c>/integrations</c>).
    /// </summary>
    public static bool IsDocsSectionPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        foreach (var prefix in DocsSectionPrefixes)
        {
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="path"/> belongs to the documentation tree, which is served by a
    /// separate container with its own bilingual layout and its own sitemap. Matches both
    /// canonical <c>/docs/...</c> paths and bare documentation section paths (<c>/guide/...</c>,
    /// <c>/api/...</c>, <c>/integrations/...</c>).
    /// </summary>
    public static bool IsDocsPath(string? path) =>
        path is not null
        && (path.Equals(DocsPath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(DocsPath + "/", StringComparison.OrdinalIgnoreCase)
            || IsDocsSectionPath(path));

    /// <summary>
    /// The documentation URL for <paramref name="culture"/>. VitePress nests its English tree
    /// <b>inside</b> the docs base (<c>/docs/en/...</c>) rather than under the site-wide <c>/en</c>
    /// prefix, so docs links cannot be prefixed like the rest of the site. Mirrors 1:1, so the tail
    /// carries over unchanged. Russian lives directly under <c>/docs/...</c>.
    /// Handles both canonical <c>/docs/...</c> paths and bare section paths (<c>/guide/...</c>, etc.).
    /// </summary>
    public static string DocsFor(string? path, string? culture)
    {
        var isEnglish = string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(path))
        {
            return isEnglish ? DocsPath + "/en/" : DocsPath + "/";
        }

        if (path.Equals(DocsPath, StringComparison.OrdinalIgnoreCase))
        {
            return isEnglish ? DocsPath + "/en/" : DocsPath + "/";
        }

        if (path.StartsWith(DocsPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = path[DocsPath.Length..];
            if (rest.Equals("/en", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("/en/", StringComparison.OrdinalIgnoreCase))
            {
                if (isEnglish)
                {
                    return rest.Length == 3 ? DocsPath + "/en/" : path;
                }

                var tail = rest[3..];
                return tail.Length == 0 ? DocsPath + "/" : DocsPath + tail;
            }

            return isEnglish ? DocsPath + "/en" + rest : path;
        }

        var normalizedBare = path.StartsWith('/') ? path : "/" + path;
        return isEnglish ? DocsPath + "/en" + normalizedBare : DocsPath + normalizedBare;
    }
}
