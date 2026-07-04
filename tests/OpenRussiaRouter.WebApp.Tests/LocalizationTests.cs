using System.Net;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// TS-010: covers the UI culture switching (cookie + Accept-Language),
/// the language switcher in the topbar, and the localized resource scan.
/// </summary>
public class LocalizationTests : IClassFixture<WebAppFactory>
{
    private const string CultureCookie = ".AspNetCore.Culture";
    private readonly WebAppFactory _factory;
    private readonly HttpClient _client;

    public LocalizationTests(WebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task HomePage_WithRussianCookie_RendersRussianNav()
    {
        var req = NewRequest("/", $"{CultureCookie}=c=ru|uic=ru");
        var body = await ReadBodyAsync(req);

        Assert.Equal("ru", ExtractHtmlLang(body));
        AssertContainsText(body, ">Модели<");
        AssertContainsText(body, ">Инструкция<");
    }

    [Fact]
    public async Task HomePage_WithEnglishCookie_RendersEnglishNav()
    {
        var req = NewRequest("/", $"{CultureCookie}=c=en|uic=en");
        var body = await ReadBodyAsync(req);

        Assert.Equal("en", ExtractHtmlLang(body));
        AssertContainsText(body, ">Models<");
        AssertContainsText(body, ">Setup guide<");
    }

    [Fact]
    public async Task HomePage_AcceptLanguage_En_FallsBackToEnglish()
    {
        var req = NewRequest("/");
        req.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        var body = await ReadBodyAsync(req);

        Assert.Equal("en", ExtractHtmlLang(body));
        AssertContainsText(body, ">Models<");
    }

    [Fact]
    public async Task HomePage_NoLanguageSignal_UsesDefaultRu()
    {
        var req = NewRequest("/");
        var body = await ReadBodyAsync(req);

        Assert.Equal("ru", ExtractHtmlLang(body));
        AssertContainsText(body, ">Модели<");
    }

    [Fact]
    public async Task HomePage_CultureCookieOverridesAcceptLanguage()
    {
        var req = NewRequest("/", $"{CultureCookie}=c=ru|uic=ru");
        req.Headers.Add("Accept-Language", "en");
        var body = await ReadBodyAsync(req);

        Assert.Equal("ru", ExtractHtmlLang(body));
        AssertContainsText(body, ">Модели<");
    }

    [Fact]
    public async Task HomePage_QueryStringCulture_IsIgnored()
    {
        // Spec: QueryStringRequestCultureProvider must NOT be in the provider chain.
        var req = NewRequest("/?culture=en&ui-culture=en");
        var body = await ReadBodyAsync(req);

        Assert.Equal("ru", ExtractHtmlLang(body));
        AssertContainsText(body, ">Модели<");
    }

    [Fact]
    public async Task Topbar_HasLanguageSwitcherForm_AndHighlightsActive()
    {
        var req = NewRequest("/", $"{CultureCookie}=c=ru|uic=ru");
        var body = await ReadBodyAsync(req);

        Assert.Contains("action=\"/preferences/language\"", body);
        Assert.Contains("value=\"ru\"", body);
        Assert.Contains("value=\"en\"", body);
        // The switcher form has the antiforgery token + returnUrl hidden field
        Assert.Contains("name=\"returnUrl\"", body);
        Assert.Contains("name=\"__RequestVerificationToken\"", body);
    }

    [Theory]
    [InlineData("/login", "ru")]
    [InlineData("/signup", "ru")]
    [InlineData("/login", "en")]
    [InlineData("/signup", "en")]
    public async Task AnonymousAuthPages_HaveCompactLanguageSwitcher(string path, string lang)
    {
        var req = NewRequest(path, $"{CultureCookie}=c={lang}|uic={lang}");
        var body = await ReadBodyAsync(req);

        Assert.Contains("action=\"/preferences/language\"", body);
        Assert.Contains("class=\"lang-switch lang-switch--compact\"", body);
    }

    [Fact]
    public async Task FooterLinks_AreLocalized()
    {
        var ruReq = NewRequest("/", $"{CultureCookie}=c=ru|uic=ru");
        var ruBody = await ReadBodyAsync(ruReq);
        AssertContainsText(ruBody, "Политика конфиденциальности");
        AssertContainsText(ruBody, "Публичная оферта");

        var enReq = NewRequest("/", $"{CultureCookie}=c=en|uic=en");
        var enBody = await ReadBodyAsync(enReq);
        AssertContainsText(enBody, "Privacy Policy");
        AssertContainsText(enBody, "Public Offer");
    }

    [Fact]
    public async Task LoginPage_ServerErrorMessage_LocalizedInBothCultures()
    {
        var ruReq = NewPostRequest("/login", $"{CultureCookie}=c=ru|uic=ru", new Dictionary<string, string>
        {
            { "Input.Email", "" },
            { "Input.Password", "" },
        });
        var ruBody = await ReadBodyAsync(ruReq);
        AssertContainsText(ruBody, "Поле обязательно для заполнения");

        var enReq = NewPostRequest("/login", $"{CultureCookie}=c=en|uic=en", new Dictionary<string, string>
        {
            { "Input.Email", "" },
            { "Input.Password", "" },
        });
        var enBody = await ReadBodyAsync(enReq);
        AssertContainsText(enBody, "This field is required");
    }

    [Fact]
    public async Task TopUp_PostUnderRussianCulture_CreatesCheckoutWithInvariantAmount()
    {
        using var factory = CreateAuthenticatedPaymentFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = NewPostRequest("/workspace/credits?handler=TopUp", $"{CultureCookie}=c=ru|uic=ru",
            new Dictionary<string, string>
            {
                ["TopUpAmount"] = "10.50",
                ["SelectedCurrency"] = "USD",
                ["AcceptLegal"] = "true"
            });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var billing = factory.Services.GetRequiredService<CheckoutFakeBilling>();
        Assert.Equal(1, billing.CreateCalls);
        Assert.Equal(10.50m, billing.LastCreate!.Value.displayAmount);
    }

    [Theory]
    [InlineData("ru", "Введите корректную сумму")]
    [InlineData("en", "Enter a valid amount")]
    public async Task TopUp_InvalidAmount_DoesNotCreateCheckout_AndShowsLocalizedError(string culture, string expected)
    {
        using var factory = CreateAuthenticatedPaymentFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = NewPostRequest("/workspace/credits?handler=TopUp",
            $"{CultureCookie}=c={culture}|uic={culture}",
            new Dictionary<string, string>
            {
                ["TopUpAmount"] = "not-an-amount",
                ["SelectedCurrency"] = "USD",
                ["AcceptLegal"] = "true"
            });

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertContainsText(body, expected);
        Assert.Equal(0, factory.Services.GetRequiredService<CheckoutFakeBilling>().CreateCalls);
    }

    /// <summary>
    /// TS-010 acceptance: every resource key referenced in any view
    /// (<c>@Localizer[...]</c> via <c>IStringLocalizer&lt;SharedResource&gt;</c>)
    /// and in any PageModel / DataAnnotations attribute must be present in both
    /// <c>.ru.resx</c> and <c>.en.resx</c>. The user must never see the raw key.
    /// </summary>
    [Fact]
    public void EveryUsedResourceKey_IsPresentInBothCultures()
    {
        var (sharedRu, sharedEn) = LoadSharedKeys();
        var webAppRoot = GetWebAppRoot();

        foreach (var key in sharedRu)
            Assert.True(sharedEn.Contains(key),
                $"SharedResource key '{key}' is in .ru.resx but missing from .en.resx.");
        foreach (var key in sharedEn)
            Assert.True(sharedRu.Contains(key),
                $"SharedResource key '{key}' is in .en.resx but missing from .ru.resx.");
        foreach (var key in LoadUsedSharedResourceKeys(webAppRoot))
        {
            Assert.True(sharedRu.Contains(key),
                $"Used resource key '{key}' is missing from .ru.resx.");
            Assert.True(sharedEn.Contains(key),
                $"Used resource key '{key}' is missing from .en.resx.");
        }

        var pagesRoot = Path.Combine(webAppRoot, "Pages");
        foreach (var viewPath in Directory.EnumerateFiles(pagesRoot, "*.cshtml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Admin{Path.DirectorySeparatorChar}")
                         && !path.EndsWith("_AdminLayout.cshtml", StringComparison.OrdinalIgnoreCase)))
        {
            var content = File.ReadAllText(viewPath);
            var keys = Regex.Matches(content, @"@Localizer\[\s*""([^""]+)""")
                .Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
            if (keys.Count == 0)
                continue;

            var relative = Path.GetRelativePath(pagesRoot, viewPath);
            var resourceBase = Path.Combine(webAppRoot, "Resources", "Pages", relative[..^".cshtml".Length]);
            var ru = LoadKeysFromAbsolute(resourceBase + ".ru.resx");
            var en = LoadKeysFromAbsolute(resourceBase + ".en.resx");
            Assert.True(ru.SetEquals(en), $"Page resources have different key sets: {resourceBase}.");
            if (content.Contains("Catalog_Category_", StringComparison.Ordinal))
            {
                foreach (var id in new[] { "Text", "Vision", "Coding" })
                    keys.Add($"Catalog_Category_{id}");
            }
            foreach (var key in keys)
            {
                Assert.True(ru.Contains(key), $"View key '{key}' is missing from {resourceBase}.ru.resx.");
                Assert.True(en.Contains(key), $"View key '{key}' is missing from {resourceBase}.en.resx.");
            }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static HttpRequestMessage NewRequest(string path, string? cookie = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        return req;
    }

    private static HttpRequestMessage NewPostRequest(string path, string? cookie, Dictionary<string, string> form)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form)
        };
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        return req;
    }

    private async Task<string> ReadBodyAsync(HttpRequestMessage req)
    {
        using var resp = await _client.SendAsync(req).ConfigureAwait(false);
        return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private WebApplicationFactory<Program> CreateAuthenticatedPaymentFactory() =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<BillingProvider>();
                services.AddSingleton<CheckoutFakeBilling>();
                services.AddSingleton<BillingProvider>(sp => sp.GetRequiredService<CheckoutFakeBilling>());
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName, _ => { });
            }));

    private static string ExtractHtmlLang(string body)
    {
        var m = Regex.Match(body, "<html\\s+lang=\"([^\"]+)\"");
        Assert.True(m.Success, $"<html lang> attribute missing. Body: {Truncate(body, 200)}");
        return m.Groups[1].Value;
    }

    /// <summary>
    /// Asserts the body contains the given text somewhere — either as-is, or in
    /// one of the forms Razor might emit. Razor's default encoder replaces
    /// non-ASCII characters with <c>&amp;#xHEX;</c> numeric character references,
    /// so we accept both the literal form and the numeric-encoded form.
    /// </summary>
    private static void AssertContainsText(string body, string literal)
    {
        if (body.Contains(literal)) return;
        if (body.Contains(System.Net.WebUtility.HtmlEncode(literal))) return;

        // Razor's <c>IHtmlEncoder</c> emits &#x{HEX}; for code points > 0x7F.
        // Build a regex that matches either the raw char or the numeric entity.
        var pattern = new System.Text.StringBuilder();
        foreach (var ch in literal)
        {
            if (ch < 0x80)
            {
                pattern.Append(System.Text.RegularExpressions.Regex.Escape(ch.ToString()));
            }
            else
            {
                pattern.Append("(?:");
                pattern.Append($"&#x{((int)ch):X};");
                pattern.Append("|");
                pattern.Append(ch);
                pattern.Append(")");
            }
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(body, pattern.ToString()))
            return;
        throw new Xunit.Sdk.XunitException(
            $"Body does not contain '{literal}' (or its Razor-encoded form). Pattern: '{pattern}'.\nFirst 2000:\n{Truncate(body, 2000)}");
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private static (HashSet<string> Ru, HashSet<string> En) LoadSharedKeys()
    {
        var ru = LoadKeysFrom(Path.Combine("Resources", "SharedResource.ru.resx"));
        var en = LoadKeysFrom(Path.Combine("Resources", "SharedResource.en.resx"));
        return (ru, en);
    }

    private static HashSet<string> LoadUsedSharedResourceKeys(string webAppRoot)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var localizerPattern = new Regex(@"(?:localizer|_localizer)\[\s*""([^""]+)""");
        var annotationPattern = new Regex(@"ErrorMessageResourceName\s*=\s*""([^""]+)""");

        foreach (var path in Directory.EnumerateFiles(webAppRoot, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(path);
            foreach (Match match in localizerPattern.Matches(content))
                keys.Add(match.Groups[1].Value);
            foreach (Match match in annotationPattern.Matches(content))
                keys.Add(match.Groups[1].Value);
        }

        return keys;
    }

    private static HashSet<string> LoadKeysFrom(string relativePath)
    {
        var full = Path.Combine(GetWebAppRoot(), relativePath);
        return LoadKeysFromAbsolute(full);
    }

    private static HashSet<string> LoadKeysFromAbsolute(string full)
    {
        Assert.True(File.Exists(full), $"Resource file not found: {full}");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var doc = new XmlDocument();
        doc.Load(full);
        foreach (XmlNode node in doc.GetElementsByTagName("data"))
        {
            var name = node.Attributes?["name"]?.Value;
            if (!string.IsNullOrEmpty(name)) keys.Add(name);
        }
        return keys;
    }

    private static string GetWebAppRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "OpenRussiaRouter.WebApp"));
}
