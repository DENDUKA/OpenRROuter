using System.Net;
using FluentAssertions;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class LegalResponsiveTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task AppCss_ContainsLegalDocumentResponsiveRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".legal-document {");
        css.Should().Contain("overflow-wrap: break-word;");
        css.Should().Contain(".legal-document h1 {");
        css.Should().Contain(".legal-document a {");
        css.Should().Contain("@media (max-width: 480px) {");
        css.Should().Contain("h1 { font-size: 28px; }");
    }

    [Fact]
    public async Task AppMinCss_ContainsLegalDocumentResponsiveRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.min.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".legal-document{max-width:800px;margin:0 auto;line-height:1.7;overflow-wrap:break-word;word-break:normal;}");
        css.Should().Contain(".legal-document h1{margin-bottom:var(--spacing-xl);overflow-wrap:break-word;word-break:normal;}");
        css.Should().Contain(".legal-document a{overflow-wrap:break-word;word-break:break-word;}");
        css.Should().Contain("@media (max-width:480px){h1{font-size:28px;}");
    }

    [Theory]
    [InlineData("/ru/legal/privacy")]
    [InlineData("/en/legal/privacy")]
    [InlineData("/ru/legal/offer")]
    [InlineData("/en/legal/offer")]
    public async Task LegalPages_RenderWithinLegalDocumentContainer(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("<div class=\"legal-document\">");
        html.Should().Contain("<h1");
    }
}
