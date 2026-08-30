using System.Net;
using FluentAssertions;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class FooterResponsiveTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public void Layout_ContainsFooterStructure()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "OpenRussiaRouter.WebApp", "Pages", "Shared", "_Layout.cshtml");
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);
        content.Should().Contain("class=\"app-footer\"");
        content.Should().Contain("class=\"app-footer-content\"");
    }

    [Fact]
    public async Task AppCss_ContainsFooterWrapRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".app-footer {");
        css.Should().Contain(".app-footer-content {");
        css.Should().Contain("flex-wrap: wrap;");
        css.Should().Contain("gap: var(--spacing-md) var(--spacing-xl);");
    }

    [Fact]
    public async Task AppMinCss_ContainsFooterWrapRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.min.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".app-footer{border-top:1px solid var(--color-hairline);padding:var(--spacing-lg) var(--spacing-xl);background:var(--color-canvas);}");
        css.Should().Contain(".app-footer-content{display:flex;flex-wrap:wrap;gap:var(--spacing-md) var(--spacing-xl);justify-content:center;font-size:13px;color:var(--color-body-mid);}");
    }
}
