using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Controllers;

[Route("providers/logo")]
public sealed class PublicProviderLogoController(BillingProvider billingProvider) : Controller
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetLogo(string id, CancellationToken ct)
    {
        var provider = await billingProvider.GetModelProviderAsync(id, ct);

        // Real uploaded logo wins. If a provider has no logo (common on a fresh
        // deployment — logos are uploaded via Admin → Providers), fall back to a
        // generated monogram badge instead of a 404/broken image. Serving it here
        // fixes every <img src="/providers/logo/…"> render site at once (home strip,
        // providers cards, model rows) with no view changes.
        if (provider?.LogoBytes is { Length: > 0 } && provider.LogoContentType is not null)
        {
            Response.Headers.CacheControl = "public, max-age=604800";
            return File(provider.LogoBytes, provider.LogoContentType);
        }

        var label = provider?.DisplayName is { Length: > 0 } name ? name : id;
        var svg = BuildMonogramSvg(id, label);
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(Encoding.UTF8.GetBytes(svg), "image/svg+xml; charset=utf-8");
    }

    // Muted, tasteful palette — deterministically picked per provider id so the same
    // provider always gets the same colour across pages and reloads.
    private static readonly string[] Palette =
    [
        "#4F6BED", "#7C5CDB", "#0E9F6E", "#D97706", "#DC2626",
        "#0891B2", "#DB2777", "#5B6472", "#2563EB", "#65A30D",
    ];

    private static string BuildMonogramSvg(string id, string label)
    {
        var letter = FirstLetter(label);
        // Stable hash → colour index (no Math.Random; must be identical every request).
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        var color = Palette[hash[0] % Palette.Length];
        var text = System.Security.SecurityElement.Escape(letter);

        return
            $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-hidden="true">
              <rect width="64" height="64" rx="14" fill="{color}"/>
              <text x="32" y="33" fill="#FFFFFF" font-family="-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif" font-size="34" font-weight="600" text-anchor="middle" dominant-baseline="central">{text}</text>
            </svg>
            """;
    }

    private static string FirstLetter(string label)
    {
        foreach (var ch in label.Trim())
        {
            if (char.IsLetterOrDigit(ch))
                return char.ToUpperInvariant(ch).ToString();
        }
        return "?";
    }
}
