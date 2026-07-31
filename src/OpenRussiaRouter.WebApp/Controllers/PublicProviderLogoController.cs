using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Markup;

namespace OpenRussiaRouter.WebApp.Controllers;

[Route("providers/logo")]
public sealed class PublicProviderLogoController(BillingProvider billingProvider, IMemoryCache cache) : Controller
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLogo(string id, CancellationToken ct)
    {
        var cacheKey = $"provider-logo:{id}";
        var provider = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await billingProvider.GetModelProviderAsync(id, ct);
        });

        // Real uploaded logo wins. If a provider has no logo (common on a fresh
        // deployment — logos are uploaded via Admin → Providers), fall back to a
        // generated monogram badge instead of a 404/broken image. Serving it here
        // fixes every <img src="/providers/logo/…"> render site at once (home strip,
        // providers cards, model rows) with no view changes.
        if (provider?.LogoBytes is { Length: > 0 } && provider.LogoContentType is not null)
        {
            var etag = $"\"{provider.UpdatedAtUtc.Ticks}\"";
            if (IfNoneMatch(etag))
                return StatusCode(StatusCodes.Status304NotModified);

            Response.Headers.CacheControl = "public, max-age=604800";
            Response.Headers.ETag = etag;
            Response.Headers.XContentTypeOptions = "nosniff";
            Response.Headers.ContentDisposition = "inline";
            return File(provider.LogoBytes, provider.LogoContentType);
        }

        var label = provider?.DisplayName is { Length: > 0 } name ? name : id;
        var svg = MonogramSvg.Build(id, label);
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(Encoding.UTF8.GetBytes(svg), "image/svg+xml; charset=utf-8");
    }

    private bool IfNoneMatch(string etag)
    {
        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        return string.Equals(ifNoneMatch, etag, StringComparison.Ordinal);
    }
}
