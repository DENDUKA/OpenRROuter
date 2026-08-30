using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class FlagshipCarouselResponsiveTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Theory]
    [InlineData("/ru")]
    [InlineData("/en")]
    public async Task Get_Home_CarouselAlignsWithContentBoxAndClipsOverflowAsync(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // .flagship-showcase should have overflow-x: clip and decorative ::before should not overflow right.
        body.Should().Contain("overflow-x: clip;");
        body.Should().Contain(".flagship-showcase::before");
        body.Should().Contain("right: 0;");

        // .flagship-carousel should align with container content area (margin-inline: 0, padding-inline: 0).
        var carouselRule = Regex.Match(body, @"\.flagship-carousel\s*\{[^}]*\}").Value;
        carouselRule.Should().Contain("margin-inline: 0;");
        carouselRule.Should().Contain("padding-inline: 0;");
        carouselRule.Should().NotContain("calc(50% - 50vw)");

        // Mask gradient should remain intact across visible bounds.
        carouselRule.Should().Contain("mask-image: linear-gradient(90deg, transparent, #000 42px, #000 calc(100% - 42px), transparent);");
    }
}
