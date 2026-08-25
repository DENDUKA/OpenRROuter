# Explicit RU Locale URL Prefix Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Implement explicit `/ru` URL prefix routing for Russian language across `OpenRussiaRouter.WebApp` (mirroring the `/en` prefix), ensuring symmetrical URL structure (`/ru/models` and `/en/models`), 301 redirects from legacy bare public paths to `/ru`, and updated canonical, hreflang, sitemap, tag helper, and test coverage.

**Architecture:** Extend the existing `PathBase`-based localization pipeline in ASP.NET Core (`LocalePrefixMiddleware` + `LocaleUrl`). Both `/ru` and `/en` will be treated as first-class addressable path prefixes that get stripped into `Request.PathBase`. Bare public routes (e.g. `/`, `/models`, `/providers`) will 301-redirect to `/ru/...`, while machine endpoints (`/health`, `/metrics`, `/webhook/*`, `/internal/*`, static assets) remain unprefixed.

**Tech Stack:** .NET 8, ASP.NET Core Razor Pages, C# 12, xUnit, FluentAssertions.

---

### Task 1: Update `LocaleUrl` to support `/ru` prefix

**Objective:** Make `LocaleUrl` return `"/ru"` for Russian in `PrefixFor`, update `For` and `Alternates` to emit `/ru` prefixes for Russian URLs, and keep `DocsFor` properly routed.

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Services/Localization/LocaleUrl.cs`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/LocaleUrlTests.cs`

**Step 1: Write failing unit tests in `LocaleUrlTests.cs`**

```csharp
[Theory]
[InlineData("/", "ru", "/ru")]
[InlineData("/models", "ru", "/ru/models")]
[InlineData("/models/xiaomi/mimo-v2.5-pro", "ru", "/ru/models/xiaomi/mimo-v2.5-pro")]
[InlineData("/", "en", "/en")]
[InlineData("/models", "en", "/en/models")]
[InlineData("/models/xiaomi/mimo-v2.5-pro", "en", "/en/models/xiaomi/mimo-v2.5-pro")]
public void For_BuildsThePublicPathOfALocale(string path, string culture, string expected) =>
    LocaleUrl.For(path, culture).Should().Be(expected);

[Fact]
public void Alternates_AreAbsolute_AndCoverEveryAddressableCulture()
{
    var alternates = LocaleUrl.Alternates("https://example.com/", "/tools").ToList();

    alternates.Should().Equal(
        ("ru", "https://example.com/ru/tools"),
        ("en", "https://example.com/en/tools"));
}
```

**Step 2: Run test to verify failure**

Run: `dotnet test --filter FullyQualifiedName~LocaleUrlTests.For_BuildsThePublicPathOfALocale`
Expected: FAIL — Russian paths returned without `/ru` prefix.

**Step 3: Update `LocaleUrl.cs` implementation**

```csharp
namespace OpenRussiaRouter.WebApp.Services.Localization;

public static class LocaleUrl
{
    public const string RussianPrefix = "/ru";
    public const string EnglishPrefix = "/en";

    public static readonly string[] AddressableCultures = ["ru", "en"];
    public const string XDefaultCulture = "en";

    public static string PrefixFor(string? culture) =>
        string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase) ? EnglishPrefix :
        string.Equals(culture, "ru", StringComparison.OrdinalIgnoreCase) ? RussianPrefix :
        RussianPrefix;

    public static (string Culture, string Path) Split(string? path)
    {
        var value = string.IsNullOrEmpty(path) ? "/" : path;

        foreach (var culture in AddressableCultures)
        {
            var prefix = "/" + culture;
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = value[prefix.Length..];
            if (rest.Length == 0)
            {
                return (culture, "/");
            }

            if (rest[0] == '/')
            {
                return (culture, rest);
            }
        }

        return (SupportedCultures.DefaultCulture, value);
    }

    public static string For(string? path, string? culture)
    {
        var bare = string.IsNullOrEmpty(path) ? "/" : path;
        var prefix = PrefixFor(culture);

        return bare == "/" ? prefix : prefix + bare;
    }

    public static IEnumerable<(string Culture, string Url)> Alternates(string baseUrl, string? path)
    {
        var origin = baseUrl.TrimEnd('/');
        foreach (var culture in AddressableCultures)
        {
            yield return (culture, origin + For(path, culture));
        }
    }

    public const string DocsPath = "/docs";

    public static bool IsDocsPath(string? path) =>
        path is not null
        && (path.Equals(DocsPath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(DocsPath + "/", StringComparison.OrdinalIgnoreCase));

    public static string DocsFor(string? path, string? culture)
    {
        var bare = string.IsNullOrEmpty(path) ? DocsPath : path;
        if (string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase))
        {
            var rest = bare[DocsPath.Length..];
            return rest.Length == 0 ? DocsPath + "/en/" : DocsPath + "/en" + rest;
        }

        return bare.Length == DocsPath.Length ? DocsPath + "/" : bare;
    }
}
```

**Step 4: Run test to verify pass**

Run: `dotnet test --filter FullyQualifiedName~LocaleUrlTests.For_BuildsThePublicPathOfALocale`
Expected: PASS

---

### Task 2: Update `LocalePrefixMiddleware` for explicit `/ru` handling and bare route redirects

**Objective:** Handle `/ru` prefix in `LocalePrefixMiddleware` by putting `/ru` into `Request.PathBase` (same as `/en`), redirecting uppercase `/RU` to `/ru`, trailing slash `/ru/` to `/ru`, docs requests `/ru/docs*` to `/docs*`, and redirecting legacy bare public URLs to `/ru/...`.

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Services/Localization/LocalePrefixMiddleware.cs`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/LocalizationTests.cs`

**Step 1: Write failing test in `LocalizationTests.cs`**

```csharp
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

[Theory]
[InlineData("/models", "/ru/models")]
[InlineData("/providers", "/ru/providers")]
[InlineData("/tools", "/ru/tools")]
[InlineData("/support", "/ru/support")]
[InlineData("/legal/privacy", "/ru/legal/privacy")]
[InlineData("/ru/", "/ru")]
[InlineData("/RU/models", "/ru/models")]
public async Task LegacyBareAndDuplicateRoutes_301ToRussianCanonical(string requested, string expected)
{
    using var response = await _client.SendAsync(NewRequest(requested));

    Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    Assert.Equal(expected, response.Headers.Location!.ToString());
}
```

**Step 2: Run test to verify failure**

Run: `dotnet test --filter FullyQualifiedName~LocalizationTests.RussianPrefixedUrl_Returns200AndRendersRussian_WithoutAnyCookie`
Expected: FAIL

**Step 3: Update `LocalePrefixMiddleware.cs`**

```csharp
namespace OpenRussiaRouter.WebApp.Services.Localization;

public sealed class LocalePrefixMiddleware(RequestDelegate next)
{
    internal const string CultureItemKey = "orr.locale.culture";

    private static readonly string[] NonLocalizedPrefixes =
    [
        "/webhook", "/internal", "/health", "/metrics", "/.well-known",
        "/sitemap.xml", "/robots.txt", "/og-image.png", "/favicon.png",
        "/docs", "/signin-google", "/oauth-yandex"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var (culture, barePath) = LocaleUrl.Split(path);
        var hasPrefix = !string.Equals(barePath, path, StringComparison.Ordinal);

        var isSafeMethod = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);

        if (!hasPrefix)
        {
            // If path is a public indexable page or root, redirect to /ru/...
            if (isSafeMethod && ShouldRedirectBarePathToDefaultLocale(path))
            {
                Redirect(context, LocaleUrl.For(path, SupportedCultures.DefaultCulture));
                return;
            }

            await next(context);
            return;
        }

        if (isSafeMethod)
        {
            // "/RU/models" -> "/ru/models", "/EN/models" -> "/en/models"
            var prefix = path[..(path.Length - (barePath == "/" ? 0 : barePath.Length))];
            var canonicalPrefix = LocaleUrl.PrefixFor(culture);
            if (!string.Equals(prefix, canonicalPrefix, StringComparison.Ordinal))
            {
                Redirect(context, LocaleUrl.For(barePath, culture));
                return;
            }

            // Docs are served by VitePress container at /docs/ (ru) and /docs/en/ (en)
            if (LocaleUrl.IsDocsPath(barePath))
            {
                Redirect(context, LocaleUrl.DocsFor(barePath, culture));
                return;
            }
        }

        context.Request.PathBase = context.Request.PathBase.Add(LocaleUrl.PrefixFor(culture));
        context.Request.Path = barePath;
        context.Items[CultureItemKey] = culture;

        await next(context);
    }

    private static bool ShouldRedirectBarePathToDefaultLocale(string path)
    {
        if (path == "/") return true;

        foreach (var nonLocalized in NonLocalizedPrefixes)
        {
            if (path.Equals(nonLocalized, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(nonLocalized + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Private / auth / user workspaces remain unprefixed
        if (path.StartsWith("/workspace", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/signup", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/logout", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/preferences", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static void Redirect(HttpContext context, string target) =>
        context.Response.Redirect(
            context.Request.PathBase + target + context.Request.QueryString,
            permanent: true);
}
```

**Step 4: Run test to verify pass**

Run: `dotnet test --filter FullyQualifiedName~LocalizationTests.RussianPrefixedUrl_Returns200AndRendersRussian_WithoutAnyCookie`
Expected: PASS

---

### Task 3: Update `PreferencesController` and `LocalePathTagHelper`

**Objective:** Ensure language switcher return URLs translate seamlessly between `/ru/...` and `/en/...`, and `LocalePathTagHelper` rewrites internal links to `/ru/...` when browsing under `/ru`.

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Controllers/PreferencesController.cs`
- Modify: `src/OpenRussiaRouter.WebApp/TagHelpers/LocalePathTagHelper.cs`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/LocaleUrlTests.cs`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/LanguagePreferenceTests.cs`

**Step 1: Write failing tests in `LocaleUrlTests.cs`**

```csharp
[Theory]
[InlineData("/models", "/ru/models")]
[InlineData("/", "/ru")]
[InlineData("/models?category=text", "/ru/models?category=text")]
[InlineData("/support#form", "/ru/support#form")]
public void Localize_PrefixesRootRelativeLinks_ForRussian(string href, string expected) =>
    LocalePathTagHelper.Localize(href, "/ru").Should().Be(expected);

[Theory]
[InlineData("/en/models", "ru", "/ru/models")]
[InlineData("/ru/models", "en", "/en/models")]
[InlineData("/ru", "en", "/en")]
[InlineData("/en", "ru", "/ru")]
[InlineData("/models", "ru", "/ru/models")]
[InlineData("/models", "en", "/en/models")]
public void TranslateReturnUrl_RepointsTheUrlAtTheTargetLocale(
    string returnUrl, string culture, string expected) =>
    PreferencesController.TranslateReturnUrl(returnUrl, culture).Should().Be(expected);
```

**Step 2: Run test to verify failure**

Run: `dotnet test --filter FullyQualifiedName~LocaleUrlTests.TranslateReturnUrl_RepointsTheUrlAtTheTargetLocale`
Expected: FAIL

**Step 3: Update `PreferencesController.cs` and `LocalePathTagHelper.cs`**

Verify `PreferencesController.TranslateReturnUrl` and `LocalePathTagHelper.Localize` properly use `LocaleUrl.For(bare, culture)` and handle `/ru` and `/en` uniformly.

**Step 4: Run test to verify pass**

Run: `dotnet test --filter FullyQualifiedName~LocaleUrlTests.TranslateReturnUrl_RepointsTheUrlAtTheTargetLocale`
Expected: PASS

---

### Task 4: Update `_Layout.cshtml` for Canonical URLs & Reciprocal Hreflang

**Objective:** Ensure `<link rel="canonical">` and `<link rel="alternate" hreflang="...">` output `/ru` URLs for Russian and `/en` URLs for English, avoiding any trailing slash inconsistencies.

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Shared/_Layout.cshtml`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/SeoHeadTests.cs`

**Step 1: Write failing tests in `SeoHeadTests.cs`**

```csharp
[Fact]
public async Task Get_RussianHome_CanonicalIsSelfReferencing_WithoutTrailingSlash()
{
    using var factory = CreateFactory();
    using var client = factory.CreateClient();

    using var response = await client.GetAsync("/ru");
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/ru\" />");
    body.Should().NotContain("<link rel=\"canonical\" href=\"https://example.com/ru/\" />");
    body.Should().Contain("<link rel=\"alternate\" hreflang=\"ru\" href=\"https://example.com/ru\" />");
    body.Should().Contain("<link rel=\"alternate\" hreflang=\"en\" href=\"https://example.com/en\" />");
    body.Should().Contain("<link rel=\"alternate\" hreflang=\"x-default\" href=\"https://example.com/en\" />");
}
```

**Step 2: Run test to verify failure**

Run: `dotnet test --filter FullyQualifiedName~SeoHeadTests.Get_RussianHome_CanonicalIsSelfReferencing_WithoutTrailingSlash`
Expected: FAIL

**Step 3: Update `_Layout.cshtml` canonical URL calculation**

Ensure that for both `/ru` and `/en`:
```csharp
if (string.IsNullOrEmpty(canonicalUrl))
{
    if (path == "/")
    {
        canonicalUrl = string.IsNullOrEmpty(Context.Request.PathBase.Value)
            ? baseUrl + "/ru"
            : baseUrl + Context.Request.PathBase.Value;
    }
    else
    {
        canonicalUrl = baseUrl + Context.Request.PathBase + path;
    }
}
```

**Step 4: Run test to verify pass**

Run: `dotnet test --filter FullyQualifiedName~SeoHeadTests.Get_RussianHome_CanonicalIsSelfReferencing_WithoutTrailingSlash`
Expected: PASS

---

### Task 5: Update `SitemapEndpoint` for `/ru` and `/en` localized URLs

**Objective:** Ensure dynamic XML sitemap publishes `<loc>https://example.com/ru/...</loc>` and `<loc>https://example.com/en/...</loc>` for all indexable pages with full reciprocal hreflang annotations.

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Api/SitemapEndpoint.cs`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/SitemapEndpointTests.cs`

**Step 1: Write failing tests in `SitemapEndpointTests.cs`**

```csharp
[Fact]
public void BuildXml_LocalizedEntry_EmitsBothLocales_WithRuAndEnLocs()
{
    var xml = SitemapEndpoint.BuildXml(
        "https://example.com",
        [new SitemapEndpoint.SitemapEntry("/models")]);

    var doc = XDocument.Parse(xml);
    XNamespace sm = "http://www.sitemaps.org/schemas/sitemap/0.9";
    XNamespace xhtml = "http://www.w3.org/1999/xhtml";

    var urls = doc.Root!.Elements(sm + "url").ToList();
    urls.Select(u => u.Element(sm + "loc")!.Value).Should().Equal(
        "https://example.com/ru/models",
        "https://example.com/en/models");
}

[Fact]
public void BuildXml_HomePage_RussianLocIsPrefixWithoutTrailingSlash()
{
    var xml = SitemapEndpoint.BuildXml(
        "https://example.com",
        [new SitemapEndpoint.SitemapEntry("/")]);

    xml.Should().Contain("<loc>https://example.com/ru</loc>");
    xml.Should().NotContain("<loc>https://example.com/ru/</loc>");
    xml.Should().Contain("<loc>https://example.com/en</loc>");
    xml.Should().NotContain("<loc>https://example.com/en/</loc>");
}
```

**Step 2: Run test to verify failure**

Run: `dotnet test --filter FullyQualifiedName~SitemapEndpointTests.BuildXml_LocalizedEntry_EmitsBothLocales_WithRuAndEnLocs`
Expected: FAIL

**Step 3: Update `SitemapEndpoint.cs`**

Verify `SitemapEndpoint.BuildXml` uses `LocaleUrl.Alternates(origin, entry.Path)` to produce both `<url>` blocks and reciprocal `<xhtml:link>` elements.

**Step 4: Run test to verify pass**

Run: `dotnet test --filter FullyQualifiedName~SitemapEndpointTests.BuildXml_LocalizedEntry_EmitsBothLocales_WithRuAndEnLocs`
Expected: PASS

---

### Task 6: Comprehensive Test Suite Updates & Validation

**Objective:** Update existing tests across all test suites (`SeoHeadTests`, `LocalizationTests`, `LanguagePreferenceTests`, `LogoutTests`, `OAuthLocaleTests`) to reflect explicit `/ru` URLs.

**Files:**
- Modify: `tests/OpenRussiaRouter.WebApp.Tests/SeoHeadTests.cs`
- Modify: `tests/OpenRussiaRouter.WebApp.Tests/LocalizationTests.cs`
- Modify: `tests/OpenRussiaRouter.WebApp.Tests/LanguagePreferenceTests.cs`
- Modify: `tests/OpenRussiaRouter.WebApp.Tests/LogoutTests.cs`
- Modify: `tests/OpenRussiaRouter.WebApp.Tests/SitemapEndpointTests.cs`

**Step 1: Update test expectations**
- Update assertions expecting bare `/models` to expect `/ru/models` for Russian canonicals/hreflangs.
- Update tests asserting bare `/` redirects to `/ru`.
- Ensure `/ru/logout` is covered in `LogoutTests`.

**Step 2: Run entire test suite**

Run: `dotnet test`
Expected: Total tests passed, 0 failures.

---

### Task 7: Update Project Documentation

**Objective:** Keep project documentation (`src/OpenRussiaRouter.WebApp/CLAUDE.md`, `CLAUDE.md`, `DOCUMENTATION.md`) aligned with the new symmetrical `/ru` and `/en` URL scheme.

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/CLAUDE.md`
- Modify: `CLAUDE.md`
- Modify: `docs/DOCUMENTATION.md`

**Step 1: Update documentation sections**
- Update routing notes: Russian is served under `/ru`, English under `/en`.
- Update SEO & sitemap rules: 301 redirects for bare public URLs to `/ru`, self-referencing canonicals for `/ru/...` and `/en/...`.

---

## Plan Review Checklist

- [x] Tasks are sequential and logical
- [x] Each task is bite-sized (2-5 min)
- [x] File paths are exact
- [x] Code examples and test assertions are complete
- [x] Commands are exact with expected output
- [x] DRY, YAGNI, TDD principles applied
- [x] Preserves machine endpoints, auth, and VitePress docs compatibility
