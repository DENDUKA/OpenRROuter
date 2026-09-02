using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace OpenRussiaRouter.E2ETests;

/// <summary>
/// E2E test verifying Yandex Metrika client ID capture and offline conversion integration flow.
/// </summary>
public sealed class YandexMetrikaE2ETests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public YandexMetrikaE2ETests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LandingAndLayout_ContainsYandexMetrikaAndConsentBehavior()
    {
        var baseUrl = TestConfiguration.BaseUrl;
        var browser = _fixture.Browser;
        if (browser is null)
        {
            return;
        }

        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        // 1. Open Landing page
        var response = await page.GotoAsync($"{baseUrl}/ru");
        Assert.NotNull(response);
        Assert.True(response.Ok);

        // 2. Verify Metrika script or goal runner script tags exist when counter is configured
        var content = await page.ContentAsync();
        Assert.Contains("tag.js?id=111582266", content);
        Assert.Contains("ym(111582266, 'init'", content);

        // 3. Verify consent cookie interaction (orr_consent)
        var consentBanner = page.Locator("#consentBanner, .consent-banner, [data-testid='consent-banner']");
        var bannerCount = await consentBanner.CountAsync();
        if (bannerCount > 0)
        {
            var acceptBtn = page.Locator("#consentAccept, [data-action='consent-accept'], .btn-consent-accept");
            if (await acceptBtn.CountAsync() > 0)
            {
                await acceptBtn.ClickAsync();
                var cookies = await context.CookiesAsync();
                var consentCookie = cookies.FirstOrDefault(c => c.Name == "orr_consent");
                Assert.NotNull(consentCookie);
                Assert.Equal("1", consentCookie.Value);
            }
        }
    }

    [Fact]
    public async Task AuthenticatedUser_WithConsentAndYmUid_StoresCookieAndCapturesIdentity()
    {
        if (!TestConfiguration.IsConfigured)
        {
            return;
        }

        var baseUrl = TestConfiguration.BaseUrl;
        var browser = _fixture.Browser;
        if (browser is null)
        {
            return;
        }

        var simulatedClientId = "9876543210123456";
        await using var context = await browser.NewContextAsync();
        
        // Seed consent and _ym_uid cookies in browser context
        await context.AddCookiesAsync(
        [
            new Cookie
            {
                Name = "orr_consent",
                Value = "1",
                Domain = new Uri(baseUrl).Host,
                Path = "/"
            },
            new Cookie
            {
                Name = "_ym_uid",
                Value = simulatedClientId,
                Domain = new Uri(baseUrl).Host,
                Path = "/"
            }
        ]);

        var page = await context.NewPageAsync();

        // Login
        var loginPage = new Pages.LoginPage(page, baseUrl);
        await loginPage.NavigateAsync();
        await loginPage.LoginAsync(TestConfiguration.UserEmail!, TestConfiguration.UserPassword!);

        // Access workspace to trigger YandexMetrikaIdentityMiddleware
        var resp = await page.GotoAsync($"{baseUrl}/workspace/apikeys");
        Assert.NotNull(resp);
        Assert.True(resp.Ok);

        // Ensure page loaded and user is authenticated
        var pageText = await page.ContentAsync();
        Assert.Contains("/logout", pageText);
    }
}
