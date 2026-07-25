using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.WebApp.Services.SiteStats;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class SiteVisitTrackingTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/models", "/models")]
    [InlineData("/workspace/api-keys", "/workspace/api-keys")]
    public void NormalizePath_PlainPaths_AreKeptAsIs(string? path, string expected)
    {
        SiteVisitTrackingMiddleware.NormalizePath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("/models/12345", "/models/{id}")]
    [InlineData("/invite/8f14e45fceea167a5a36dedd4bea2543", "/invite/{id}")]
    [InlineData("/admin/nodes/7/limits", "/admin/nodes/{id}/limits")]
    public void NormalizePath_IdSegments_AreCollapsed(string path, string expected)
    {
        SiteVisitTrackingMiddleware.NormalizePath(path).Should().Be(expected);
    }

    [Fact]
    public void NormalizePath_LongSingleSegment_IsTreatedAsIdentifier()
    {
        var path = "/" + new string('a', 500);

        SiteVisitTrackingMiddleware.NormalizePath(path).Should().Be("/{id}");
    }

    [Fact]
    public void NormalizePath_ManyShortSegments_IsTruncated()
    {
        var path = string.Concat(Enumerable.Repeat("/ab", 100));

        SiteVisitTrackingMiddleware.NormalizePath(path).Length.Should().Be(200);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    public void ClassifyReferrer_NoUsableReferer_IsDirect(string? referer)
    {
        var (host, kind) = SiteVisitTrackingMiddleware.ClassifyReferrer(referer, "openrussiarouter.com");

        host.Should().BeNull();
        kind.Should().Be(SiteVisitReferrerKinds.Direct);
    }

    [Fact]
    public void ClassifyReferrer_OwnHost_IsInternal()
    {
        var (host, kind) = SiteVisitTrackingMiddleware.ClassifyReferrer(
            "https://openrussiarouter.com/models", "openrussiarouter.com");

        host.Should().Be("openrussiarouter.com");
        kind.Should().Be(SiteVisitReferrerKinds.Internal);
    }

    [Theory]
    [InlineData("https://www.google.com/search?q=llm", "www.google.com", "search")]
    [InlineData("https://yandex.ru/search/?text=llm", "yandex.ru", "search")]
    [InlineData("https://t.me/somechannel", "t.me", "social")]
    [InlineData("https://habr.com/ru/post/1", "habr.com", "social")]
    [InlineData("https://example.org/blog", "example.org", "external")]
    public void ClassifyReferrer_ExternalHosts_AreBucketed(string referer, string expectedHost, string expectedKind)
    {
        var (host, kind) = SiteVisitTrackingMiddleware.ClassifyReferrer(referer, "openrussiarouter.com");

        host.Should().Be(expectedHost);
        kind.Should().Be(expectedKind);
    }

    [Fact]
    public void ClassifyReferrer_KeepsHostOnly_AndDropsQuery()
    {
        var (host, _) = SiteVisitTrackingMiddleware.ClassifyReferrer(
            "https://example.org/page?email=someone@example.com", "openrussiarouter.com");

        host.Should().Be("example.org");
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120", "desktop")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0) Mobile/15E148", "mobile")]
    [InlineData("Mozilla/5.0 (Linux; Android 13) Mobile Safari", "mobile")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0) Safari", "tablet")]
    [InlineData("Mozilla/5.0 (compatible; YandexBot/3.0)", "bot")]
    [InlineData("curl/8.4.0", "bot")]
    [InlineData("", "bot")]
    [InlineData(null, "bot")]
    public void ClassifyDevice_MapsUserAgentToClass(string? userAgent, string expected)
    {
        SiteVisitTrackingMiddleware.ClassifyDevice(userAgent).Should().Be(expected);
    }
}
