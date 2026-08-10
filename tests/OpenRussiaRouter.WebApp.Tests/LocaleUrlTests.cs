using FluentAssertions;
using OpenRussiaRouter.WebApp.Controllers;
using OpenRussiaRouter.WebApp.Services.Localization;
using OpenRussiaRouter.WebApp.TagHelpers;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// SEO-024: unit coverage for the locale-prefixed URL contract. Everything that emits a public URL
/// — the middleware, the link tag helper, hreflang in the layout, the sitemap and the language
/// switcher — is built on <see cref="LocaleUrl"/>, so these rules are the ones that must not drift.
/// </summary>
public sealed class LocaleUrlTests
{
    [Theory]
    [InlineData("/", "ru", "/")]
    [InlineData("/models", "ru", "/models")]
    // The English home page is "/en", never "/en/": the canonical normalizer redirects the
    // trailing-slash form away, so emitting it anywhere would publish a URL that 301s.
    [InlineData("/", "en", "/en")]
    [InlineData("/models", "en", "/en/models")]
    [InlineData("/models/xiaomi/mimo-v2.5-pro", "en", "/en/models/xiaomi/mimo-v2.5-pro")]
    public void For_BuildsThePublicPathOfALocale(string path, string culture, string expected) =>
        LocaleUrl.For(path, culture).Should().Be(expected);

    [Theory]
    [InlineData("/en/models", "en", "/models")]
    [InlineData("/en", "en", "/")]
    [InlineData("/ru/models", "ru", "/models")]
    [InlineData("/models", "ru", "/models")]
    [InlineData("/", "ru", "/")]
    public void Split_SeparatesLocaleFromPath(string input, string culture, string path)
    {
        var result = LocaleUrl.Split(input);
        result.Culture.Should().Be(culture);
        result.Path.Should().Be(path);
    }

    [Theory]
    [InlineData("/entrypoint")]
    [InlineData("/english")]
    [InlineData("/runtime")]
    public void Split_OnlyMatchesWholeSegments(string path)
    {
        // "/entrypoint" must not be read as "/en" + "trypoint".
        var result = LocaleUrl.Split(path);
        result.Culture.Should().Be("ru");
        result.Path.Should().Be(path);
    }

    [Fact]
    public void Alternates_AreAbsolute_AndCoverEveryAddressableCulture()
    {
        var alternates = LocaleUrl.Alternates("https://example.com/", "/tools").ToList();

        alternates.Should().Equal(
            ("ru", "https://example.com/tools"),
            ("en", "https://example.com/en/tools"));
    }

    [Fact]
    public void XDefault_IsEnglish()
    {
        // The version for visitors whose language matches neither. Pointing it at Russian would
        // hand every non-RU-speaking market a page they cannot read.
        LocaleUrl.XDefaultCulture.Should().Be("en");
    }

    [Theory]
    [InlineData("/docs", "en", "/docs/en/")]
    [InlineData("/docs/guide", "en", "/docs/en/guide")]
    [InlineData("/docs", "ru", "/docs")]
    [InlineData("/docs/guide", "ru", "/docs/guide")]
    public void DocsFor_NestsEnglishInsideTheDocsBase(string path, string culture, string expected) =>
        // VitePress serves /docs/ and /docs/en/ from its own container; docs links cannot take the
        // site-wide /en prefix.
        LocaleUrl.DocsFor(path, culture).Should().Be(expected);

    // ── link rewriting ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("/models", "/en/models")]
    [InlineData("/", "/en")]
    [InlineData("/models?category=text", "/en/models?category=text")]
    [InlineData("/support#form", "/en/support#form")]
    public void Localize_PrefixesRootRelativeLinks(string href, string expected) =>
        LocalePathTagHelper.Localize(href, "/en").Should().Be(expected);

    [Theory]
    [InlineData("https://example.com/models")]   // absolute
    [InlineData("//cdn.example.com/x.png")]      // protocol-relative
    [InlineData("#anchor")]
    [InlineData("mailto:support@example.com")]
    [InlineData("/en/models")]                   // already inside the contour
    [InlineData("/sitemap.xml")]                 // defined to live at the origin root
    [InlineData("/robots.txt")]
    [InlineData("/webhook/rollypay")]            // machine endpoint, no UI
    [InlineData("/health")]
    public void Localize_LeavesNonLocalizableTargetsAlone(string href) =>
        LocalePathTagHelper.Localize(href, "/en").Should().BeNull();

    [Fact]
    public void Localize_DocsLink_GoesToTheDocsEnglishTree_NotUnderThePrefix() =>
        LocalePathTagHelper.Localize("/docs/guide", "/en").Should().Be("/docs/en/guide");

    [Fact]
    public void Localize_IsANoOpOutsideTheLocaleContour() =>
        LocalePathTagHelper.Localize("/models", string.Empty).Should().BeNull();

    // ── language switch ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("/models", "en", "/en/models")]
    [InlineData("/en/models", "ru", "/models")]
    [InlineData("/en/models", "en", "/en/models")]
    [InlineData("/models?category=text", "en", "/en/models?category=text")]
    [InlineData("/en/tools#list", "ru", "/tools#list")]
    [InlineData("/workspace/apikeys", "en", "/en/workspace/apikeys")]
    public void TranslateReturnUrl_RepointsTheUrlAtTheTargetLocale(
        string returnUrl, string culture, string expected) =>
        PreferencesController.TranslateReturnUrl(returnUrl, culture).Should().Be(expected);
}
