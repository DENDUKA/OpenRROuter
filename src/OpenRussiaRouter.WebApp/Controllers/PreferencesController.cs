using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Localization;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Controllers;

/// <summary>
/// Lightweight per-user UI preferences that need to be changeable from the topbar
/// (i.e. from any page), so they live in a controller rather than a single page handler.
/// </summary>
[Authorize]
[Route("preferences")]
public sealed class PreferencesController(
    AuthProvider authProvider,
    ILogger<PreferencesController> logger) : Controller
{
    /// <summary>Cookie that mirrors the saved display currency so the topbar can show the active choice without a profile round-trip.</summary>
    public const string CurrencyCookie = "orr_ccy";

    [HttpPost("currency")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCurrency([FromForm] string currency, [FromForm] string? returnUrl)
    {
        if (currency is "RUB" or "USD" or "EUR")
        {
            try
            {
                var userId = User.GetUserId();
                var user = await authProvider.GetUserAsync(userId, HttpContext.RequestAborted);
                if (user != null)
                {
                    await authProvider.UpdateUserAsync(
                        userId,
                        displayName: user.DisplayName,
                        maxApiKeys: user.MaxApiKeys,
                        displayCurrency: currency,
                        cancellationToken: HttpContext.RequestAborted);
                    SetCurrencyCookie(Response, currency);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to set display currency. UserId={UserId}", User.GetUserId());
            }
        }

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToPage("/Models");
    }

    /// <summary>
    /// TS-010: switch the UI culture via cookie. Anonymous — the switcher must work on
    /// <c>/login</c> and <c>/signup</c> as well as inside the workspace.
    /// Invalid <paramref name="culture"/> values are silently ignored (no cookie set),
    /// but the redirect still happens.
    /// </summary>
    [HttpPost("language")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage([FromForm] string culture, [FromForm] string? returnUrl)
    {
        if (SupportedCultures.IsSupported(culture))
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Path = "/",
                    MaxAge = TimeSpan.FromDays(365),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax
                });
        }

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToPage("/Index");
    }

    /// <summary>Shared so the Settings page write-path keeps the topbar cookie in sync.</summary>
    public static void SetCurrencyCookie(HttpResponse response, string currency) =>
        response.Cookies.Append(CurrencyCookie, currency, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true
        });

    /// <summary>Cookie that stores the user's analytics consent choice (1 = accepted, absent = no consent).</summary>
    public const string ConsentCookie = "orr_consent";

    /// <summary>
    /// Records the user's cookie consent decision. Called from the consent banner via fetch().
    /// The cookie is read server-side in _Layout.cshtml to decide whether to load analytics tags
    /// in granted or denied mode.
    /// </summary>
    [HttpPost("consent")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult SetConsent([FromForm] string consent)
    {
        var value = consent == "1" ? "1" : "0";
        Response.Cookies.Append(ConsentCookie, value, new CookieOptions
        {
            Path = "/",
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true
        });

        return Ok();
    }
}
