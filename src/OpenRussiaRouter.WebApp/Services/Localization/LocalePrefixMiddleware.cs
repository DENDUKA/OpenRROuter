namespace OpenRussiaRouter.WebApp.Services.Localization;

/// <summary>
/// Turns the <c>/en</c> URL prefix into a locale, so English is addressable by search engines.
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
/// <c>/en/app.css</c>), before the canonical normalizer (which must see the bare path) and before
/// <c>UseRequestLocalization</c> (which reads what this middleware stored).
/// </para>
/// </remarks>
public sealed class LocalePrefixMiddleware(RequestDelegate next)
{
    /// <summary>Key under which the resolved culture is handed to <see cref="RouteCultureProvider"/>.</summary>
    internal const string CultureItemKey = "rwt.locale.culture";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var (culture, barePath) = LocaleUrl.Split(path);
        var hasPrefix = !string.Equals(barePath, path, StringComparison.Ordinal);

        if (!hasPrefix)
        {
            // Unprefixed URL: the default (Russian) locale. Cookie still applies for the private
            // areas, so nothing is stored here — RouteCultureProvider stays out of the way.
            await next(context);
            return;
        }

        var isSafeMethod = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);

        // Russian is the unprefixed locale. "/ru/..." is therefore a duplicate address for a page
        // that already has a canonical one — fold it away instead of serving the same content twice.
        if (string.Equals(culture, SupportedCultures.DefaultCulture, StringComparison.OrdinalIgnoreCase))
        {
            if (isSafeMethod)
            {
                Redirect(context, barePath);
                return;
            }

            context.Request.Path = barePath;
            await next(context);
            return;
        }

        if (isSafeMethod)
        {
            // "/EN/models" and "/en/models" are the same page; keep one spelling indexable.
            var prefix = path[..(path.Length - (barePath == "/" ? 0 : barePath.Length))];
            var canonicalPrefix = LocaleUrl.PrefixFor(culture);
            if (!string.Equals(prefix, canonicalPrefix, StringComparison.Ordinal))
            {
                Redirect(context, LocaleUrl.For(barePath, culture));
                return;
            }

            // Documentation is served by the VitePress container, which owns its own bilingual tree
            // and its own sitemap. Serving /en/docs here would resurrect the Razor page that Caddy
            // shadows in production and split the docs cluster across two URLs.
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
