using FluentAssertions;
using OpenRussiaRouter.WebApp.Controllers;
using OpenRussiaRouter.WebApp.Services.Localization;
using OpenRussiaRouter.WebApp.TagHelpers;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Unit coverage for the locale-prefixed URL contract (/ru and /en). Everything that emits a public URL
/// — the middleware, the link tag helper, hreflang in the layout, the sitemap and the language
/// switcher — is built on <see cref="LocaleUrl"/>, so these rules are the ones that must not drift.
/// </summary>
public sealed class LocaleUrlTests
{
    [Theory]
    [InlineData("/", "ru", "/ru")]
    [InlineData("/models", "ru", "/ru/models")]
    [InlineData("/models/xiaomi/mimo-v2.5-pro", "ru", "/ru/models/xiaomi/mimo-v2.5-pro")]
    [InlineData("/", "en", "/en")]
    [InlineData("/models", "en", "/en/models")]
    [InlineData("/models/xiaomi/mimo-v2.5-pro", "en", "/en/models/xiaomi/mimo-v2.5-pro")]
    public void For_BuildsThePublicPathOfALocale(string path, string culture, string expected) =>
        LocaleUrl.For(path, culture).Should().Be(expected);

    [Theory]
    [InlineData("/en/models", "en", "/models")]
    [InlineData("/en", "en", "/")]
    [InlineData("/ru/models", "ru", "/models")]
    [InlineData("/ru", "ru", "/")]
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
    [InlineData("/russia")]
    public void Split_OnlyMatchesWholeSegments(string path)
    {
        // "/entrypoint" must not be read as "/en" + "trypoint", nor "/russia" as "/ru" + "ssia".
        var result = LocaleUrl.Split(path);
        result.Culture.Should().Be("ru");
        result.Path.Should().Be(path);
    }

    [Fact]
    public void Alternates_AreAbsolute_AndCoverEveryAddressableCulture()
    {
        var alternates = LocaleUrl.Alternates("https://example.com/", "/tools").ToList();

        alternates.Should().Equal(
            ("ru", "https://example.com/ru/tools"),
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
    [InlineData("/guide", true)]
    [InlineData("/guide/", true)]
    [InlineData("/guide/api-keys", true)]
    [InlineData("/api", true)]
    [InlineData("/api/", true)]
    [InlineData("/api/chat-completions", true)]
    [InlineData("/integrations", true)]
    [InlineData("/integrations/", true)]
    [InlineData("/integrations/ide", true)]
    [InlineData("/guidelines", false)]
    [InlineData("/apikeys", false)]
    [InlineData("/ai-api", false)]
    [InlineData("/integration", false)]
    [InlineData("/models", false)]
    [InlineData("/", false)]
    public void IsDocsSectionPath_MatchesOnlyKnownDocsSectionPrefixes(string path, bool expected) =>
        LocaleUrl.IsDocsSectionPath(path).Should().Be(expected);

    [Theory]
    [InlineData("/docs", true)]
    [InlineData("/docs/", true)]
    [InlineData("/docs/guide", true)]
    [InlineData("/guide", true)]
    [InlineData("/guide/api-keys", true)]
    [InlineData("/api", true)]
    [InlineData("/api/reference", true)]
    [InlineData("/integrations", true)]
    [InlineData("/integrations/ui-clients", true)]
    [InlineData("/models", false)]
    [InlineData("/providers", false)]
    public void IsDocsPath_MatchesBothDocsPrefixAndSectionPrefixes(string path, bool expected) =>
        LocaleUrl.IsDocsPath(path).Should().Be(expected);

    [Theory]
    [InlineData("/docs", "en", "/docs/en/")]
    [InlineData("/docs/guide", "en", "/docs/en/guide")]
    [InlineData("/docs", "ru", "/docs/")]
    [InlineData("/docs/guide", "ru", "/docs/guide")]
    [InlineData("/guide", "en", "/docs/en/guide")]
    [InlineData("/guide", "ru", "/docs/guide")]
    [InlineData("/guide/api-keys", "en", "/docs/en/guide/api-keys")]
    [InlineData("/guide/api-keys", "ru", "/docs/guide/api-keys")]
    [InlineData("/api", "en", "/docs/en/api")]
    [InlineData("/api", "ru", "/docs/api")]
    [InlineData("/api/chat-completions", "en", "/docs/en/api/chat-completions")]
    [InlineData("/api/chat-completions", "ru", "/docs/api/chat-completions")]
    [InlineData("/integrations", "en", "/docs/en/integrations")]
    [InlineData("/integrations", "ru", "/docs/integrations")]
    [InlineData("/integrations/ide", "en", "/docs/en/integrations/ide")]
    [InlineData("/integrations/ide", "ru", "/docs/integrations/ide")]
    public void DocsFor_NestsEnglishInsideTheDocsBase(string path, string culture, string expected) =>
        // VitePress serves /docs/ and /docs/en/ from its own container; docs links cannot take the
        // site-wide /en or /ru prefix.
        LocaleUrl.DocsFor(path, culture).Should().Be(expected);

    // ── link rewriting ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("/models", "/en/models")]
    [InlineData("/", "/en")]
    [InlineData("/models?category=text", "/en/models?category=text")]
    [InlineData("/support#form", "/en/support#form")]
    public void Localize_PrefixesRootRelativeLinks_ForEnglish(string href, string expected) =>
        LocalePathTagHelper.Localize(href, "/en").Should().Be(expected);

    [Theory]
    [InlineData("/models", "/ru/models")]
    [InlineData("/", "/ru")]
    [InlineData("/models?category=text", "/ru/models?category=text")]
    [InlineData("/support#form", "/ru/support#form")]
    public void Localize_PrefixesRootRelativeLinks_ForRussian(string href, string expected) =>
        LocalePathTagHelper.Localize(href, "/ru").Should().Be(expected);

    [Theory]
    [InlineData("https://example.com/models")]   // absolute
    [InlineData("//cdn.example.com/x.png")]      // protocol-relative
    [InlineData("#anchor")]
    [InlineData("mailto:support@example.com")]
    [InlineData("/en/models")]                   // already inside the contour
    [InlineData("/ru/models")]
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
    [InlineData("/models", "ru", "/ru/models")]
    [InlineData("/en/models", "ru", "/ru/models")]
    [InlineData("/ru/models", "en", "/en/models")]
    [InlineData("/en/models", "en", "/en/models")]
    [InlineData("/ru/models", "ru", "/ru/models")]
    [InlineData("/models?category=text", "en", "/en/models?category=text")]
    [InlineData("/models?category=text", "ru", "/ru/models?category=text")]
    [InlineData("/en/tools#list", "ru", "/ru/tools#list")]
    [InlineData("/ru/tools#list", "en", "/en/tools#list")]
    [InlineData("/workspace/apikeys", "en", "/en/workspace/apikeys")]
    [InlineData("/workspace/apikeys", "ru", "/ru/workspace/apikeys")]
    public void TranslateReturnUrl_RepointsTheUrlAtTheTargetLocale(
        string returnUrl, string culture, string expected) =>
        PreferencesController.TranslateReturnUrl(returnUrl, culture).Should().Be(expected);
}
