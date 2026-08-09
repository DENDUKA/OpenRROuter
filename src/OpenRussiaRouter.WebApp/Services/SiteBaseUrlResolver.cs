namespace OpenRussiaRouter.WebApp.Services;

/// <summary>
/// Resolves the canonical public site origin (no trailing slash) used for crawler-facing SEO
/// metadata only: <c>&lt;link rel="canonical"&gt;</c>, <c>og:url</c>, <c>og:image</c> /
/// <c>twitter:image</c> and the <c>sitemap.xml</c> <c>&lt;loc&gt;</c> entries. Resolution order:
/// <c>PublicSiteBaseUrl</c> config → current request origin.
/// Deliberately separate from <see cref="ApiBaseUrlResolver"/>: the API base URL is admin-
/// configurable via <c>platform_settings.public_api_base_url</c> and points at the api subdomain
/// (<c>https://api.openrussiarouter.com</c>), which must never leak into crawler metadata — search
/// engines only index the canonical site. The site origin is a fixed deployment property (the apex
/// domain), so it is read from configuration, not from the admin setting or the API base.
/// </summary>
public sealed class SiteBaseUrlResolver(IConfiguration configuration)
{
    public string Resolve(HttpRequest request)
    {
        var configured = configuration["PublicSiteBaseUrl"];

        return !string.IsNullOrWhiteSpace(configured)
            ? configured.TrimEnd('/')
            : $"{request.Scheme}://{request.Host}";
    }
}
