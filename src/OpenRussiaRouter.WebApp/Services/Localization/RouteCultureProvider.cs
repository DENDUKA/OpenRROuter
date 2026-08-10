using Microsoft.AspNetCore.Localization;

namespace OpenRussiaRouter.WebApp.Services.Localization;

/// <summary>
/// Reads the culture that <see cref="LocalePrefixMiddleware"/> lifted out of the URL.
/// </summary>
/// <remarks>
/// First in the provider chain, so the URL beats the cookie: a visitor who arrives on
/// <c>/en/models</c> from a search result must get English even if an earlier visit left
/// <c>c=ru</c> in their cookie — otherwise the URL and the content disagree and the page
/// contradicts its own <c>hreflang</c>. On unprefixed paths this provider abstains and the cookie
/// decides, which is what keeps the private areas (workspace, admin) cookie-driven.
/// </remarks>
public sealed class RouteCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Items.TryGetValue(LocalePrefixMiddleware.CultureItemKey, out var value)
            && value is string culture)
        {
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture, culture));
        }

        return NullProviderCultureResult;
    }
}
