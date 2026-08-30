using System.Net;
using FluentAssertions;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class TopbarLogoResponsiveTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public void Layout_ContainsTopbarLogoElement()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "OpenRussiaRouter.WebApp", "Pages", "Shared", "_Layout.cshtml");
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);
        content.Should().Contain("class=\"topbar-logo\"");
    }

    [Fact]
    public async Task AppCss_ContainsFittingTopbarLogoRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".topbar-logo {");
        css.Should().Contain("height: 40px;");
        css.Should().Contain("object-fit: contain;");
    }

    [Fact]
    public async Task AppMinCss_ContainsFittingTopbarLogoRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.min.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".topbar-logo{height:40px;width:auto;max-width:100%;object-fit:contain;display:block;}");
    }
}
