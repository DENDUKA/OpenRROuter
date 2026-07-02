using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("admin/providers/logo")]
public sealed class ProviderLogoController(BillingProvider billingProvider) : Controller
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetLogo(string id, CancellationToken ct)
    {
        var provider = await billingProvider.GetModelProviderAsync(id, ct);
        if (provider?.LogoBytes is not { Length: > 0 } || provider.LogoContentType is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=604800";
        return File(provider.LogoBytes, provider.LogoContentType);
    }
}
