using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Content;

namespace OpenRussiaRouter.WebApp.Controllers;

/// <summary>
/// Serves images embedded into a tool's markdown. Public and unauthenticated: the markdown has to
/// render for anonymous visitors, and the uuid is the only handle to the row.
/// </summary>
[Route("tools/image")]
public sealed class PublicToolImageController(ContentProvider contentProvider) : Controller
{
    // The :guid constraint keeps malformed ids from ever reaching the service.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var image = await contentProvider.GetToolImageAsync(id, ct);
        if (image is null)
        {
            return NotFound();
        }

        // Content is immutable per uuid — a replaced screenshot gets a new id.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        // The stored content type is derived from magic bytes server-side, but be explicit:
        // this endpoint echoes a stored value back to browsers.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition = "inline";
        return File(image.Bytes, image.ContentType);
    }
}
