using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.WebApp.Services.Analytics;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Localization;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Pages.Login;

public sealed class ExternalCallbackModel(
    AuthProvider AuthProvider,
    BillingProvider? BillingProvider = null,
    ILogger<ExternalCallbackModel>? Logger = null,
    ISupportBotNotifier? BotNotifier = null,
    IYandexMetrikaIdentityStore? IdentityStore = null) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var result = await HttpContext.AuthenticateAsync("External");
        if (!result.Succeeded)
            return RedirectToPage("/Login");

        var provider = result.Properties?.Items.TryGetValue("provider", out var p) == true ? p : null;
        var providerSub = result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = result.Principal!.FindFirstValue(ClaimTypes.Email);

        if (provider is null || providerSub is null || email is null)
            return RedirectToPage("/Login");

        var loginResult = await AuthProvider.LoginOrRegisterWithExternalProviderAsync(provider, providerSub, email);
        if (loginResult is null)
            return RedirectToPage("/Login");

        if (result.Properties?.Items.TryGetValue("culture", out var culture) == true
            && SupportedCultures.IsSupported(culture)
            && culture is not null)
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

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, loginResult.UserId),
            new Claim(ClaimTypes.Email, loginResult.Email),
            new Claim(ClaimTypes.Role, loginResult.Role),
            new Claim("session_token", loginResult.SessionToken),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
        await HttpContext.SignOutAsync("External");

        if (loginResult.IsNewUser)
        {
            var duplicates = new List<DuplicateAccountInfo>();
            if (IdentityStore is not null && HttpContext.Request.Cookies.TryGetValue("_ym_uid", out var ymUid) && !string.IsNullOrWhiteSpace(ymUid))
            {
                try
                {
                    var relatedUserIds = await IdentityStore.FindRelatedUserIdsAsync(ymUid, loginResult.UserId, HttpContext.RequestAborted);
                    foreach (var relatedId in relatedUserIds)
                    {
                        var relatedUser = await AuthProvider.GetUserAsync(relatedId, HttpContext.RequestAborted);
                        if (relatedUser is not null)
                        {
                            var balance = BillingProvider is not null
                                ? await BillingProvider.GetBalanceAsync(relatedId, HttpContext.RequestAborted)
                                : null;
                            duplicates.Add(new DuplicateAccountInfo(relatedUser.Email, balance?.Amount ?? 0m));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Failed to resolve duplicate accounts via Yandex Metrica identity for UserId={UserId}", loginResult.UserId);
                }
            }

            BotNotifier?.NotifyUserRegistered(loginResult.Email, duplicates.Count > 0, duplicates);

            // Grant start balance if promotion is enabled (best-effort)
            if (BillingProvider is not null)
            {
                try
                {
                    var enabledRaw = await BillingProvider.GetPlatformSettingAsync(
                        PlatformSettingKeys.PromotionsStartBalanceEnabled, HttpContext.RequestAborted);
                    if (bool.TryParse(enabledRaw, out var enabled) && enabled)
                    {
                        var amountUsdRaw = await BillingProvider.GetPlatformSettingAsync(
                            PlatformSettingKeys.PromotionsStartBalanceAmountUsd, HttpContext.RequestAborted);
                        if (decimal.TryParse(amountUsdRaw, System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture, out var amountUsd) && amountUsd > 0)
                        {
                            await BillingProvider.GrantStartBalanceAsync(loginResult.UserId, amountUsd, HttpContext.RequestAborted);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Failed to grant start balance after external login registration. UserId={UserId}", loginResult.UserId);
                }
            }

            // This branch has no verification step — the provider already vouched for the
            // mailbox — so both funnel steps close here. Without it every OAuth registration was
            // invisible: the counters only ever saw signups that went through /verify-email.
            AnalyticsTempData.Enqueue(
                TempData,
                AnalyticsGoal.Named(AnalyticsGoals.Signup),
                AnalyticsGoal.Named(AnalyticsGoals.EmailVerified));
        }

        return loginResult.Role == "Admin"
            ? RedirectToPage("/Admin/Index")
            : RedirectToPage("/Workspace/ApiKeys");
    }
}
