using System.Text;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Content;
using OpenRussiaRouter.WebApp.Services.Markup;

namespace OpenRussiaRouter.WebApp.Controllers;

/// <summary>
/// Serves a tool's logo, falling back to a generated monogram so every
/// <c>&lt;img src="/tools/logo/…"&gt;</c> render site works without a per-view null check.
/// </summary>
[Route("tools/logo")]
public sealed class PublicToolLogoController(ContentProvider contentProvider) : Controller
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetLogo(string id, CancellationToken ct)
    {
        var logo = await contentProvider.GetToolLogoAsync(id, ct);

        if (logo is not null)
        {
            Response.Headers.CacheControl = "public, max-age=604800";
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(logo.Bytes, logo.ContentType);
        }

        var svg = MonogramSvg.Build(id, id);
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(Encoding.UTF8.GetBytes(svg), "image/svg+xml; charset=utf-8");
    }
}
