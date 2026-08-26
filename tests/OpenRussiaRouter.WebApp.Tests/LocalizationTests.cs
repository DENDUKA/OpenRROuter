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
/// TS-010: covers the UI culture switching (cookie + URL prefix),
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
    public async Task RussianHome_WithRussianCookie_RendersRussianNav()
    {
        var req = NewRequest("/ru", $"{CultureCookie}=c=ru|uic=ru");
        var body = await ReadBodyAsync(req);

        Assert.Equal("ru", ExtractHtmlLang(body));
        AssertContainsText(body, ">Модели<");
        AssertContainsText(body, ">Инструкция<");
    }

    [Fact]
    public async Task EnglishHome_WithEnglishCookie_RendersEnglishNav()
    {
        var req = NewRequest("/en", $"{CultureCookie}=c=en|uic=en");
        var body = await ReadBodyAsync(req);

        Assert.Equal("en", ExtractHtmlLang(body));
        AssertContainsText(body, ">Models<");
        AssertContainsText(body, ">Setup guide<");
    }

    [Theory]
    [InlineData("/en")]
    [InlineData("/en/models")]
    [InlineData("/en/providers")]
    [InlineData("/en/tools")]
    [InlineData("/en/support")]
    [InlineData("/en/legal/privacy")]
    public async Task EnglishPrefixedUrl_Returns200AndRendersEnglish_WithoutAnyCookie(string path)
    {
        using var response = await _client.SendAsync(NewRequest(path));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("en", ExtractHtmlLang(body));
    }

    [Theory]
    [InlineData("/ru")]
    [InlineData("/ru/models")]
    [InlineData("/ru/providers")]
    [InlineData("/ru/tools")]
    [InlineData("/ru/support")]
    [InlineData("/ru/legal/privacy")]
    public async Task RussianPrefixedUrl_Returns200AndRendersRussian_WithoutAnyCookie(string path)
    {
        using var response = await _client.SendAsync(NewRequest(path));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ru", ExtractHtmlLang(body));
    }

    [Fact]
    public async Task EnglishPrefixedUrl_BeatsRussianCookie()
    {
        var req = NewRequest("/en/models", $"{CultureCookie}=c=ru|uic=ru");
        var body = await ReadBodyAsync(req);

        Assert.Equal("en", ExtractHtmlLang(body));
        AssertContainsText(body, ">Models<");
    }

    [Fact]
    public async Task RussianPrefixedUrl_BeatsEnglishCookie()
    {
        var req = NewRequest("/ru/models", $"{CultureCookie}=c=en|uic=en");
        var body = await ReadBodyAsync(req);

        Assert.Equal("ru", ExtractHtmlLang(body));
        AssertContainsText(body, ">Модели<");
    }

    [Fact]
    public async Task EnglishPage_InternalLinks_StayInsideTheEnglishTree()
    {
        var body = await ReadBodyAsync(NewRequest("/en"));

        Assert.Contains("href=\"/en/models\"", body);
        Assert.DoesNotContain("href=\"/models\"", body);
    }

    [Fact]
    public async Task RussianPage_InternalLinks_StayInsideTheRussianTree()
    {
        var body = await ReadBodyAsync(NewRequest("/ru"));

        Assert.Contains("href=\"/ru/models\"", body);
        Assert.DoesNotContain("href=\"/models\"", body);
    }

    [Theory]
    [InlineData("/", "/ru")]
    [InlineData("/models", "/ru/models")]
    [InlineData("/providers", "/ru/providers")]
    [InlineData("/tools", "/ru/tools")]
    [InlineData("/support", "/ru/support")]
    [InlineData("/legal/privacy", "/ru/legal/privacy")]
    [InlineData("/legal/offer", "/ru/legal/offer")]
    [InlineData("/ru/", "/ru")]
    [InlineData("/RU/models", "/ru/models")]
    [InlineData("/en/", "/en")]
    [InlineData("/EN/models", "/en/models")]
    public async Task DuplicateAndBareRoutes_301ToTheCanonicalUrl(string requested, string expected)
    {
        using var response = await _client.SendAsync(NewRequest(requested));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task EnglishDocs_301ToTheVitePressEnglishTree()
    {
        using var response = await _client.SendAsync(NewRequest("/en/docs"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/docs/en/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task RussianDocs_301ToTheVitePressRussianTree()
    {
        using var response = await _client.SendAsync(NewRequest("/ru/docs"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/docs/", response.Headers.Location!.ToString());
    }

    [Theory]
    // Bare docs section paths (without /docs/) -> 301 to /docs/...
    [InlineData("/guide/api-keys", "/docs/guide/api-keys")]
    [InlineData("/guide/billing", "/docs/guide/billing")]
    [InlineData("/api/chat-completions", "/docs/api/chat-completions")]
    [InlineData("/api/reference", "/docs/api/reference")]
    [InlineData("/integrations/frameworks", "/docs/integrations/frameworks")]
    [InlineData("/integrations/ide", "/docs/integrations/ide")]
    [InlineData("/guide", "/docs/guide")]
    [InlineData("/api", "/docs/api")]
    [InlineData("/integrations", "/docs/integrations")]
    // English-prefixed docs section paths -> 301 to /docs/en/...
    [InlineData("/en/guide/api-keys", "/docs/en/guide/api-keys")]
    [InlineData("/en/guide/billing", "/docs/en/guide/billing")]
    [InlineData("/en/api/chat-completions", "/docs/en/api/chat-completions")]
    [InlineData("/en/api/reference", "/docs/en/api/reference")]
    [InlineData("/en/integrations/frameworks", "/docs/en/integrations/frameworks")]
    [InlineData("/en/integrations/ide", "/docs/en/integrations/ide")]
    [InlineData("/en/guide", "/docs/en/guide")]
    [InlineData("/en/api", "/docs/en/api")]
    [InlineData("/en/integrations", "/docs/en/integrations")]
    // Russian-prefixed docs section paths -> 301 to /docs/...
    [InlineData("/ru/guide/api-keys", "/docs/guide/api-keys")]
    [InlineData("/ru/guide/billing", "/docs/guide/billing")]
    [InlineData("/ru/api/chat-completions", "/docs/api/chat-completions")]
    [InlineData("/ru/api/reference", "/docs/api/reference")]
    [InlineData("/ru/integrations/frameworks", "/docs/integrations/frameworks")]
    [InlineData("/ru/integrations/ide", "/docs/integrations/ide")]
    [InlineData("/ru/guide", "/docs/guide")]
    [InlineData("/ru/api", "/docs/api")]
    [InlineData("/ru/integrations", "/docs/integrations")]
    public async Task DocsSectionRoutes_WithoutDocsPrefix_301ToCanonicalDocsUrl(string requested, string expected)
    {
        using var response = await _client.SendAsync(NewRequest(requested));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/guide/api-keys?tab=python&ref=google", "/docs/guide/api-keys?tab=python&ref=google")]
    [InlineData("/en/api/chat-completions?model=gpt-4o", "/docs/en/api/chat-completions?model=gpt-4o")]
    [InlineData("/ru/integrations/ide?v=2", "/docs/integrations/ide?v=2")]
    public async Task DocsSectionRoutes_PreserveQueryString_On301Redirect(string requested, string expected)
    {
        using var response = await _client.SendAsync(NewRequest(requested));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task TrailingSlashUnderEnglishPrefix_301Preserves_ThePrefix()
    {
        using var response = await _client.SendAsync(NewRequest("/en/models/"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/en/models", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task TrailingSlashUnderRussianPrefix_301Preserves_ThePrefix()
    {
        using var response = await _client.SendAsync(NewRequest("/ru/models/"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/ru/models", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task PrivateAreas_HaveNoLocalePrefix_AndStayCookieDriven()
    {
        var req = NewRequest("/login", $"{CultureCookie}=c=en|uic=en");
        var body = await ReadBodyAsync(req);

        Assert.Equal("en", ExtractHtmlLang(body));
    }

    [Fact]
    public async Task Topbar_HasLanguageSwitcherForm_AndHighlightsActive()
    {
        var req = NewRequest("/ru", $"{CultureCookie}=c=ru|uic=ru");
        var body = await ReadBodyAsync(req);

        Assert.Contains("action=\"/preferences/language\"", body);
        Assert.Contains("value=\"ru\"", body);
        Assert.Contains("value=\"en\"", body);
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
        var ruReq = NewRequest("/ru", $"{CultureCookie}=c=ru|uic=ru");
        var ruBody = await ReadBodyAsync(ruReq);
        AssertContainsText(ruBody, "Политика конфиденциальности");
        AssertContainsText(ruBody, "Публичная оферта");

        var enReq = NewRequest("/en", $"{CultureCookie}=c=en|uic=en");
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

    private static void AssertContainsText(string body, string literal)
    {
        if (body.Contains(literal)) return;
        if (body.Contains(System.Net.WebUtility.HtmlEncode(literal))) return;

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
