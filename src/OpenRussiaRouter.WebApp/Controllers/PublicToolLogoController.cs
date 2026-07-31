using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OpenRussiaRouter.WebApp.Services.Content;
using OpenRussiaRouter.WebApp.Services.Markup;

namespace OpenRussiaRouter.WebApp.Controllers;

/// <summary>
/// Serves a tool's logo, falling back to a generated monogram so every
/// <c>&lt;img src="/tools/logo/…"&gt;</c> render site works without a per-view null check.
/// </summary>
[Route("tools/logo")]
public sealed class PublicToolLogoController(ContentProvider contentProvider, IMemoryCache cache) : Controller
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLogo(string id, CancellationToken ct)
    {
        var cacheKey = $"tool-logo:{id}";
        var cached = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var logo = await contentProvider.GetToolLogoAsync(id, ct);
            if (logo is null) return null;
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(logo.Bytes))}\"";
            return new CachedToolLogo(logo.Bytes, logo.ContentType, etag);
        });

        if (cached is not null)
        {
            if (IfNoneMatch(cached.Etag))
                return StatusCode(StatusCodes.Status304NotModified);

            Response.Headers.CacheControl = "public, max-age=604800";
            Response.Headers.ETag = cached.Etag;
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(cached.Bytes, cached.ContentType);
        }

        var svg = MonogramSvg.Build(id, id);
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(Encoding.UTF8.GetBytes(svg), "image/svg+xml; charset=utf-8");
    }

    private bool IfNoneMatch(string etag)
    {
        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        return string.Equals(ifNoneMatch, etag, StringComparison.Ordinal);
    }

    private sealed record CachedToolLogo(byte[] Bytes, string ContentType, string Etag);
}
