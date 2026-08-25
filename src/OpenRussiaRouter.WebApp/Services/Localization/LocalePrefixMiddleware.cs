using OpenRussiaRouter.WebApp.Services.Seo;

namespace OpenRussiaRouter.WebApp.Services.Localization;

/// <summary>
/// Turns the <c>/ru</c> and <c>/en</c> URL prefixes into a locale, so both Russian and English
/// are explicitly addressable by search engines and users.
/// </summary>
/// <remarks>
/// <para>
/// The prefix is moved out of <see cref="HttpRequest.Path"/> and into
/// <see cref="HttpRequest.PathBase"/>. That single move is what keeps the change small: routing
/// still matches the bare <c>@page</c> routes, and every URL produced by <c>UrlHelper</c>,
/// <c>LinkGenerator</c>, tilde paths (<c>~/app.css</c>) and
/// <see cref="LocalizedAnchorTagHelper"/> gets the prefix back automatically. No page, no page
/// route and no <c>ModelDetailRoute</c> needed to learn about locales.
/// </para>
/// <para>
/// Must run <b>before</b> <c>UseStaticFiles</c> (tilde-resolved asset URLs arrive prefixed, e.g.
/// <c>/en/app.css</c> or <c>/ru/app.css</c>), before the canonical normalizer (which must see the bare path)
/// and before <c>UseRequestLocalization</c> (which reads what this middleware stored).
/// </para>
/// </remarks>
public sealed class LocalePrefixMiddleware(RequestDelegate next)
{
    /// <summary>Key under which the resolved culture is handed to <see cref="RouteCultureProvider"/>.</summary>
    internal const string CultureItemKey = "orr.locale.culture";

    private static readonly string[] NonLocalizedPrefixes =
    [
        "/webhook", "/internal", "/health", "/metrics", "/.well-known",
        "/sitemap.xml", "/robots.txt", "/og-image.png", "/favicon.png",
        "/docs", "/signin-google", "/oauth-yandex"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var (culture, barePath) = LocaleUrl.Split(path);
        var hasPrefix = !string.Equals(barePath, path, StringComparison.Ordinal);

        var isSafeMethod = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);

        if (!hasPrefix)
        {
            // If the incoming request is a GET/HEAD to a public indexable page or root, redirect to /ru/...
            if (isSafeMethod && ShouldRedirectBarePathToDefaultLocale(path))
            {
                var normalizedPath = CanonicalUrlNormalizer.TryNormalize(path) ?? path;
                Redirect(context, LocaleUrl.For(normalizedPath, SupportedCultures.DefaultCulture));
                return;
            }

            await next(context);
            return;
        }

        if (isSafeMethod)
        {
            // "/RU/models" -> "/ru/models", "/EN/models" -> "/en/models"
            var prefix = path[..(path.Length - (barePath == "/" ? 0 : barePath.Length))];
            var canonicalPrefix = LocaleUrl.PrefixFor(culture);
            if (!string.Equals(prefix, canonicalPrefix, StringComparison.Ordinal))
            {
                Redirect(context, LocaleUrl.For(barePath, culture));
                return;
            }

            // Documentation is served by the VitePress container at /docs/ (ru) and /docs/en/ (en)
            if (LocaleUrl.IsDocsPath(barePath))
            {
                Redirect(context, LocaleUrl.DocsFor(barePath, culture));
                return;
            }
        }

        context.Request.PathBase = context.Request.PathBase.Add(LocaleUrl.PrefixFor(culture));
        context.Request.Path = barePath;
        context.Items[CultureItemKey] = culture;

        await next(context);
    }

    private static bool ShouldRedirectBarePathToDefaultLocale(string path)
    {
        if (path == "/") return true;

        foreach (var nonLocalized in NonLocalizedPrefixes)
        {
            if (path.Equals(nonLocalized, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(nonLocalized + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Private / auth / workspace / preferences areas remain unprefixed
        if (path.StartsWith("/workspace", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/signup", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/logout", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/preferences", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/verify-email", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/forgot-password", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/reset-password", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/invite", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/pay", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 301 to <paramref name="target"/>, preserving the query string. <paramref name="target"/> is a
    /// path <b>including</b> any locale prefix; the incoming prefix has not been moved into
    /// <c>PathBase</c> at this point, so it must not be prepended a second time.
    /// </summary>
    private static void Redirect(HttpContext context, string target) =>
        context.Response.Redirect(
            context.Request.PathBase + target + context.Request.QueryString,
            permanent: true);
}

public static class LocalePrefixMiddlewareExtensions
{
    public static IApplicationBuilder UseLocalePrefix(this IApplicationBuilder app) =>
        app.UseMiddleware<LocalePrefixMiddleware>();
}
